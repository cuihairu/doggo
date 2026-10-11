export interface MarketItem {
  id: string
  marketHashName: string
  latestPriceCents: number
  currency: string
  iconUrl?: string
}

/** Price list for market items; cents are rendered as the platform currency. */
export default function MarketTable({ items }: { items: MarketItem[] }) {
  return (
    <table className="table">
      <thead>
        <tr>
          <th>饰品</th>
          <th>最新价</th>
        </tr>
      </thead>
      <tbody>
        {items.map((item) => (
          <tr key={item.id}>
            <td>{item.marketHashName}</td>
            <td>
              {(item.latestPriceCents / 100).toFixed(2)} {item.currency}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
