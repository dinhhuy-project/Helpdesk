import { useQuery } from '@tanstack/react-query'
import { getHealth } from '../api/health'

export function useHealth() {
  return useQuery({
    queryKey: ['system', 'health'],
    queryFn: ({ signal }) => getHealth(signal).then(() => ({ responding: true })),
    staleTime: 15_000,
  })
}
