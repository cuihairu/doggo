export interface PrizeItem {
  id: string
  marketHashName: string
  rarity?: string
  iconUrl?: string
  priceCents: number
}

/** Card for a prize the user owns (case opening, roll room). */
export default function PrizeCard({ prize }: { prize: PrizeItem }) {
  return (
    <div className="card" style={{ textAlign: 'center' }}>
      {prize.iconUrl && (
        <img src={prize.iconUrl} alt="" style={{ width: 96, height: 96, objectFit: 'contain' }} />
      )}
      <div style={{ fontSize: 14, margin: '6px 0' }}>{prize.marketHashName}</div>
      {prize.rarity && <span className="rarity-tag">{prize.rarity}</span>}
      <div className="muted" style={{ marginTop: 6 }}>参考价 {(prize.priceCents / 100).toFixed(2)}</div>
    </div>
  )
}
