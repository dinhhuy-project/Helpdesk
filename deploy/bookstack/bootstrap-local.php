<?php
// Local setup helper for the pinned BookStack image, not a remote integration API.
// Secrets enter on stdin; this helper never returns them or prints exception details.
require '/app/www/vendor/autoload.php';
$app = require '/app/www/bootstrap/app.php';
$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();

try {
    $data = json_decode(file_get_contents('php://stdin'), true, 32, JSON_THROW_ON_ERROR);
    $host = parse_url(config('app.url'), PHP_URL_HOST);
    if (!in_array($host, ['localhost', '127.0.0.1'], true)) {
        throw new RuntimeException('Local bootstrap only');
    }
    $mode = $data['mode'] ?? '';
    if ($mode === 'cleanup') {
        BookStack\Api\ApiToken::query()->where('token_id', $data['tokenId'])
            ->where('name', 'Helpdesk local setup temporary')->delete();
        echo json_encode(['ok' => true]);
        exit;
    }
    $user = BookStack\Users\Models\User::query()->where('email', $data['email'])->first();
    if ($mode === 'admin' && !$user) {
        $default = BookStack\Users\Models\User::query()->where('email', 'admin@admin.com')->first();
        if (!$default || !$default->hasSystemRole('admin') || BookStack\Users\Models\Role::getSystemRole('admin')->users()->count() !== 1) {
            throw new RuntimeException('Existing editor requires manual setup');
        }
        $exit = Illuminate\Support\Facades\Artisan::call('bookstack:create-admin', [
            '--initial' => true, '--email' => $data['email'], '--name' => 'Helpdesk Wiki Editor', '--password' => $data['password'],
        ]);
        if ($exit !== 0) throw new RuntimeException('Cannot bootstrap editor');
        $user = BookStack\Users\Models\User::query()->where('email', $data['email'])->firstOrFail();
    }
    if (!$user || ($mode === 'admin' && !$user->hasSystemRole('admin')) ||
        ($mode === 'reader' && $user->roles()->pluck('roles.id')->map(fn ($id) => (int) $id)->all() !== [(int) $data['roleId']])) {
        throw new RuntimeException('Unexpected account role');
    }
    $token = BookStack\Api\ApiToken::query()->where('token_id', $data['tokenId'])->first();
    if ($token && ($token->user_id !== $user->id || !Illuminate\Support\Facades\Hash::check($data['tokenSecret'], $token->secret))) {
        throw new RuntimeException('Existing token differs');
    }
    if (!$token) {
        (new BookStack\Api\ApiToken())->forceFill([
            'name' => $mode === 'admin' ? 'Helpdesk local setup temporary' : 'Helpdesk knowledge read-only',
            'token_id' => $data['tokenId'], 'secret' => Illuminate\Support\Facades\Hash::make($data['tokenSecret']),
            'user_id' => $user->id, 'expires_at' => Illuminate\Support\Carbon::now()->addYear()->format('Y-m-d'),
        ])->save();
    }
    echo json_encode(['ok' => true, 'userId' => $user->id]);
} catch (Throwable $error) {
    fwrite(STDERR, 'Local BookStack bootstrap failed; check account state and the pinned image.' . PHP_EOL);
    exit(1);
}
