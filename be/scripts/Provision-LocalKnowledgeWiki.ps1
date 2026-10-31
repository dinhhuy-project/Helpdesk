param()

$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$privateDir = Join-Path $repoPath '.local/wiki'
[IO.Directory]::CreateDirectory($privateDir) | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
# Access-only ACL operations avoid requesting SACL privileges on repeated Windows runs.
if ($PSVersionTable.PSVersion.Major -ge 6) {
    $directory = New-Object IO.DirectoryInfo($privateDir)
    $acl = [IO.FileSystemAclExtensions]::GetAccessControl($directory, [Security.AccessControl.AccessControlSections]::Access)
}
else { $acl = [IO.Directory]::GetAccessControl($privateDir, [Security.AccessControl.AccessControlSections]::Access) }
$acl.SetAccessRuleProtection($true, $false)
foreach ($existingRule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) { $acl.RemoveAccessRuleSpecific($existingRule) }
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
if ($PSVersionTable.PSVersion.Major -ge 6) { [IO.FileSystemAclExtensions]::SetAccessControl($directory, $acl) }
else { [IO.Directory]::SetAccessControl($privateDir, $acl) }
$stateFile = Join-Path $privateDir 'bootstrap.json'
$envPath = Join-Path $repoPath '.env'
$envText = [IO.File]::ReadAllText($envPath)
$values = @{}
foreach ($line in ($envText -split '\r?\n')) {
    if ($line -match '^([A-Z0-9_]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim().Trim([char]34, [char]39) }
}
$baseProject = if ($values['COMPOSE_PROJECT_NAME']) { $values['COMPOSE_PROJECT_NAME'] } else { 'helpdesk' }
$wikiProject = $baseProject + '-wiki'
$baseUrl = 'http://127.0.0.1:' + $values['BOOKSTACK_PORT']
$utf8 = New-Object Text.UTF8Encoding($false)
if (Test-Path -LiteralPath $stateFile) { $state = [IO.File]::ReadAllText($stateFile) | ConvertFrom-Json }
else {
    $state = [pscustomobject]@{ editorEmail = 'wiki.editor@helpdesk.example'; editorPassword = [Guid]::NewGuid().ToString('N') + 'Aa!';
        readerEmail = 'wiki.reader@helpdesk.example'; readerPassword = [Guid]::NewGuid().ToString('N') + 'Aa!';
        tokenId = [Guid]::NewGuid().ToString('N'); tokenSecret = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N');
        bookId = 0; roleId = 0 }
    [IO.File]::WriteAllText($stateFile, ($state | ConvertTo-Json), $utf8)
}
$temporaryId = [Guid]::NewGuid().ToString('N')
$temporarySecret = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$adminHeaders = @{ Authorization = 'Token ' + $temporaryId + ':' + $temporarySecret }

Push-Location $repoPath
function Invoke-LocalBootstrap($data) {
    $reply = $data | ConvertTo-Json -Compress | docker compose -p $wikiProject --env-file .env -f docker-compose.wiki.yml exec -T --user abc bookstack php /setup/bootstrap-local.php
    if ($LASTEXITCODE -ne 0) { throw 'Local wiki bootstrap failed; credentials were not printed or reset.' }
    return (($reply -join "`n") | ConvertFrom-Json)
}
function Invoke-Wiki($method, $path, $body = $null) {
    $arguments = @{ Method = $method; Uri = $baseUrl + '/api/' + $path; Headers = $adminHeaders; TimeoutSec = 15 }
    if ($null -ne $body) { $arguments.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 10 -Compress)); $arguments.ContentType = 'application/json; charset=utf-8' }
    try { return Invoke-RestMethod @arguments }
    catch { throw ('Local wiki API operation failed: ' + $method + ' ' + ($path -split '\?')[0]) }
}
try {
    Invoke-LocalBootstrap @{ mode = 'admin'; email = $state.editorEmail; password = $state.editorPassword; tokenId = $temporaryId; tokenSecret = $temporarySecret } | Out-Null
    $roles = Invoke-Wiki GET 'roles?count=100'
    $role = @($roles.data | Where-Object { $_.display_name -eq 'Helpdesk export reader' })
    if ($role.Count -gt 1) { throw 'Ambiguous wiki reader role.' }
    if ($role.Count -eq 0) {
        $role = Invoke-Wiki POST 'roles' @{ display_name = 'Helpdesk export reader'; description = 'Read/export only the selected Helpdesk book'; permissions = @('access-api', 'content-export') }
    } else { $role = Invoke-Wiki GET ('roles/' + $role[0].id) }
    if (@($role.permissions | Where-Object { $_ -notin @('access-api', 'content-export') }).Count -gt 0) { throw 'Reader role has unexpected grants; review it before setup.' }
    $state.roleId = [int]$role.id

    $books = Invoke-Wiki GET 'books?count=100'
    $book = @($books.data | Where-Object { $_.name -eq 'Helpdesk - Huong dan ho tro' })
    if ($book.Count -gt 1) { throw 'Ambiguous source book.' }
    if ($book.Count -eq 0) { $book = Invoke-Wiki POST 'books' @{ name = 'Helpdesk - Huong dan ho tro'; description = 'Curated draft guides for Helpdesk review and publication.' } }
    else { $book = $book[0] }
    $state.bookId = [int]$book.id
    $permissionPath = 'content-permissions/book/' + $state.bookId
    $existingPermissions = Invoke-Wiki GET $permissionPath
    $overrides = @($existingPermissions.role_permissions | Where-Object { $_.role_id -ne $state.roleId } | ForEach-Object {
        @{ role_id = $_.role_id; view = [bool]$_.view; create = [bool]$_.create; update = [bool]$_.update; delete = [bool]$_.delete }
    })
    $overrides += @{ role_id = $state.roleId; view = $true; create = $false; update = $false; delete = $false }
    Invoke-Wiki PUT $permissionPath @{ role_permissions = $overrides } | Out-Null

    $users = Invoke-Wiki GET 'users?count=100'
    $reader = @($users.data | Where-Object { $_.email -eq $state.readerEmail })
    if ($reader.Count -eq 0) {
        Invoke-Wiki POST 'users' @{ name = 'Helpdesk read-only connector'; email = $state.readerEmail; password = $state.readerPassword; roles = @($state.roleId); send_invite = $false } | Out-Null
    }
    Invoke-LocalBootstrap @{ mode = 'reader'; email = $state.readerEmail; roleId = $state.roleId; tokenId = $state.tokenId; tokenSecret = $state.tokenSecret } | Out-Null

    $guides = [IO.File]::ReadAllText((Join-Path $repoPath 'deploy/bookstack/guides.json')) | ConvertFrom-Json
    $pages = Invoke-Wiki GET ('pages?count=100&filter[book_id]=' + $state.bookId)
    foreach ($guide in $guides) {
        if (@($pages.data | Where-Object { $_.name -eq $guide.name }).Count -eq 0) {
            Invoke-Wiki POST 'pages' @{ book_id = $state.bookId; name = $guide.name; markdown = $guide.markdown } | Out-Null
        }
    }
    $readHeaders = @{ Authorization = 'Token ' + $state.tokenId + ':' + $state.tokenSecret }
    $visible = Invoke-RestMethod -Uri ($baseUrl + '/api/pages?count=12&filter[book_id]=' + $state.bookId) -Headers $readHeaders -TimeoutSec 15
    if ($visible.data.Count -lt 2) { throw 'Reader cannot see the selected book pages.' }
    $firstId = $visible.data[0].id
    $export = Invoke-WebRequest -UseBasicParsing -Uri ($baseUrl + '/api/pages/' + $firstId + '/export/plaintext') -Headers $readHeaders -TimeoutSec 15
    if ($export.StatusCode -ne 200) { throw 'Reader cannot export plain text.' }

    $updates = @{ BOOKSTACK_CONNECTOR_ENABLED = 'true'; BOOKSTACK_BOOK_ID = [string]$state.bookId; BOOKSTACK_TOKEN_ID = $state.tokenId; BOOKSTACK_TOKEN_SECRET = $state.tokenSecret;
        BOOKSTACK_SOURCE_ID = 'support-wiki'; BOOKSTACK_BASE_URL = 'http://host.docker.internal:' + $values['BOOKSTACK_PORT'] + '/' }
    foreach ($name in $updates.Keys) {
        $pattern = '(?m)^\s*' + [regex]::Escape($name) + '\s*=[^\r\n]*'
        if ([regex]::IsMatch($envText, $pattern)) { $envText = [regex]::Replace($envText, $pattern, ($name + '=' + $updates[$name])) }
        else { $envText = $envText.TrimEnd([char]13, [char]10) + "`r`n" + $name + '=' + $updates[$name] + "`r`n" }
    }
    [IO.File]::WriteAllText($envPath, $envText, $utf8)
    [IO.File]::WriteAllText($stateFile, ($state | ConvertTo-Json), $utf8)
    [IO.File]::WriteAllText((Join-Path $privateDir 'credentials.txt'), "BookStack: $($values['BOOKSTACK_APP_URL'])`r`nEditor email: $($state.editorEmail)`r`nInitial password: $($state.editorPassword)`r`nReader token expires one year after its first creation. Reruns preserve accounts, grants, content and token.`r`n", $utf8)
    $secrets = @{ 'KnowledgeSources:BookStack:Enabled' = 'true'; 'KnowledgeSources:BookStack:BaseUrl' = 'http://host.docker.internal:' + $values['BOOKSTACK_PORT'] + '/';
        'KnowledgeSources:BookStack:AllowHttp' = 'true'; 'KnowledgeSources:BookStack:SourceId' = 'support-wiki'; 'KnowledgeSources:BookStack:BookId' = [string]$state.bookId;
        'KnowledgeSources:BookStack:TokenId' = $state.tokenId; 'KnowledgeSources:BookStack:TokenSecret' = $state.tokenSecret }
    $secrets | ConvertTo-Json -Compress | dotnet user-secrets set --project be | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot save backend connector secrets.' }
    Write-Output 'Wiki provisioned with two guides and a scoped read/export account. Credentials: .local/wiki/credentials.txt. Restart the API Container profile.'
}
finally {
    try { Invoke-LocalBootstrap @{ mode = 'cleanup'; tokenId = $temporaryId } | Out-Null }
    finally { Pop-Location }
}
