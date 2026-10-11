import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { fetchProfile } from '../api/auth'
import PrizeCard, { type PrizeItem } from '../components/PrizeCard'

/** "My prizes": inventory items won from case openings and roll rooms. */
export default function MyPrizesPage() {
  const [prizes, setPrizes] = useState<PrizeItem[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    fetchProfile()
      .then((profile) => api<{ items: PrizeItem[] }>(`/api/users/${profile.id}/prizes`))
      .then((result) => setPrizes(result.items))
      .catch((err) => setError(String(err.message ?? err)))
  }, [])

  return (
    <div>
      <h1 className="page-title">我的奖品</h1>
      {error && <p className="error-text">{error}</p>}
      <div className="grid cols-4">
        {prizes.map((prize) => (
          <PrizeCard key={prize.id} prize={prize} />
        ))}
      </div>
      {!error && prizes.length === 0 && <p className="muted">还没有奖品</p>}
    </div>
  )
}
