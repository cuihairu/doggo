import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import { fetchProfile } from '../api/auth'
import PrizeCard, { type PrizeItem } from '../components/PrizeCard'
import { EmptyState, ErrorBox, Skeleton } from '../components/States'

/** "My prizes": inventory items won from case openings and roll rooms. */
export default function MyPrizesPage() {
  const [prizes, setPrizes] = useState<PrizeItem[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(true)

  const load = useCallback(() => {
    setBusy(true)
    fetchProfile()
      .then((profile) => api<{ items: PrizeItem[] }>(`/api/users/${profile.id}/prizes`))
      .then((result) => setPrizes(result.items))
      .catch((err) => setError(String(err.message ?? err)))
      .finally(() => setBusy(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  return (
    <div>
      <h1 className="page-title">我的奖品</h1>
      {error && <ErrorBox detail={error} onRetry={load} />}
      {busy ? (
        <Skeleton lines={3} />
      ) : (
        <div className="grid cols-4">
          {prizes.map((prize) => (
            <PrizeCard key={prize.id} prize={prize} />
          ))}
        </div>
      )}
      {!busy && !error && prizes.length === 0 && (
        <EmptyState title="还没有奖品" hint="开箱或参与 Roll 房可获得奖品" />
      )}
    </div>
  )
}
