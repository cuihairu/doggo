import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, getAccessToken } from '../api/client'
import { trackEvent } from '../api/events'

interface CaseContent {
  title: string
  quality: number
  color: string
  imageUrl: string
  referencePriceCents: number
}

interface PlanOption {
  id: string
  upliftBps: number
  quantity: number
  minQuantity: number
  priceCents: number
}

interface ProductPlan {
  id: string
  title: string
  minQuantity: number
  maxQuantity: number
  canUseCoupon: boolean
  canUseCoin: boolean
  cashbackBps: number
  options: PlanOption[]
}

interface ProductDetail {
  id: string
  name: string
  iconUrl?: string
  finalPriceCents: number
  regularPriceCents: number
  status: number
  buyNotice?: string
  quantityTiers?: { quantity: number; priceCents: number }[]
  contents?: CaseContent[]
  plans?: ProductPlan[]
  promotion?: { discountPriceCents: number; stock: number } | null
}

interface Sku {
  planId: string | null
  optionId: string | null
  serviceMode: string
  quantity: number
  minQuantity: number
  unitPriceCents: number
  totalPriceCents: number
  cashbackBps: number
  canUseCoupon: boolean
  canUseCoin: boolean
  priceSource: string
}

interface MyCoupon {
  id: string
  title: string
  amountOffCents: number
  percentOff: number
  minOrderCents: number
  status: number
}

interface CreateOrderResult {
  orderId: string
  orderNo: string
  totalPriceCents: number
  totalCostCents: number
}

/** Case quality codes as shown on the Steam market. */
const QUALITY_TEXT: Record<number, string> = {
  1: '消费级',
  2: '工业级',
  3: '军规级',
  4: '受限级',
  5: '保密级',
  6: '隐秘级',
  7: '稀有特殊',
}

const COIN_CHOICES = [0, 50, 200]

/** What drops from this crate: a badge per contents row, quality-ranked by the server. */
function ContentsGrid({ contents }: { contents: CaseContent[] }) {
  const shown = contents.slice(0, 8)
  const hidden = contents.length - shown.length
  return (
    <div className="card">
      <div style={{ fontSize: 16, fontWeight: 600, marginBottom: 12 }}>包含内容（{contents.length} 项）</div>
      <div className="grid cols-4">
        {shown.map((item, index) => (
          <div key={index} style={{ display: 'flex', gap: 8, alignItems: 'center', padding: '8px 10px', borderRadius: 'var(--radius)', border: '1px solid var(--border)' }}>
            {item.imageUrl && (
              <img src={item.imageUrl} alt="" style={{ width: 28, height: 28, objectFit: 'contain' }} />
            )}
            <div>
              <div style={{ fontSize: 13 }}>{item.title}</div>
              <div className="muted" style={{ fontSize: 11 }}>
                {QUALITY_TEXT[item.quality] ?? item.quality} · ¥{(item.referencePriceCents / 100).toFixed(2)}
              </div>
            </div>
          </div>
        ))}
        {hidden > 0 && (
          <div className="muted" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '8px 10px', fontSize: 12 }}>
            … 其余 {hidden} 项
          </div>
        )}
      </div>
    </div>
  )
}

/** Product detail with SKU / quantity-tier selection, coin and coupon picks, and a
 * sticky buybar. Plan options price crate bundles; the quantity ladder applies to
 * plain buys without one (the server prices the whole order by tier). */
export default function ProductDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [product, setProduct] = useState<ProductDetail | null>(null)
  const [skus, setSkus] = useState<Sku[]>([])
  const [selectedSkuId, setSelectedSkuId] = useState<string | null>(null)
  const [selectedTier, setSelectedTier] = useState(0)
  const [coins, setCoins] = useState(0)
  const [couponId, setCouponId] = useState('')
  const [myCoupons, setMyCoupons] = useState<MyCoupon[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    api<ProductDetail>(`/api/products/${id}`).then(setProduct).catch((err) => setError(String(err.message ?? err)))
    api<{ items: Sku[] }>(`/api/products/${id}/skus`)
      .then((result) => setSkus(result.items))
      .catch(() => setSkus([]))
    // Funnel step 1: the detail page rendered with a concrete product (design §3.2).
    trackEvent('page_view_product', { productId: id, source: 'direct' }, `/products/${id}`)
  }, [id])

  const signedIn = getAccessToken() != null
  const selectedSku = skus.find((sku) => sku.optionId === selectedSkuId) ?? null
  const canUseCoupon = selectedSku?.canUseCoupon ?? false
  const canUseCoin = selectedSku?.canUseCoin ?? false

  useEffect(() => {
    if (!id || !signedIn || !canUseCoupon) return
    api<MyCoupon[]>('/api/coupons/mine')
      .then(setMyCoupons)
      .catch(() => setMyCoupons([]))
  }, [id, signedIn, canUseCoupon])

  /** Coin/coupon picks depend on the selected SKU, so selection changes drop them. */
  function selectSku(optionId: string | null, tier: number) {
    setSelectedSkuId(optionId)
    setSelectedTier(tier)
    setCoins(0)
    setCouponId('')
  }

  const baseTotalCents = selectedSku?.totalPriceCents ?? product?.finalPriceCents ?? 0
  const chosenCoupon = myCoupons.find((coupon) => coupon.id === couponId)
  const couponOffCents = chosenCoupon
    ? (chosenCoupon.amountOffCents ?? Math.round((baseTotalCents * chosenCoupon.percentOff) / 100))
    : 0
  const estimatedCents = Math.max(0, baseTotalCents - coins - couponOffCents)

  async function placeOrder() {
    if (!id || !product) return
    if (!signedIn) {
      navigate('/login')
      return
    }

    setError('')
    const sku = skus.find((item) => item.optionId != null && item.optionId === selectedSkuId)
    // Funnel step 2: plan/quantity decided, buy confirmed (design §3.1).
    trackEvent(
      'checkout_intent',
      { productId: id, planId: sku?.planId ?? null, optionId: sku?.optionId ?? null, quantity: selectedTier || 1 },
      `/products/${id}`
    )
    try {
      const result = await api<CreateOrderResult>('/api/orders', {
        body: {
          userId: (await api<{ id: string }>('/api/auth/me')).id,
          productId: id,
          planId: sku?.planId ?? null,
          optionId: sku?.optionId ?? null,
          entry: 1,
          serviceMode: 1,
          fulfillmentType: 0,
          payType: 0,
          quantity: selectedTier || 1,
          coinDeduction: coins,
          userCouponId: chosenCoupon?.id ?? null,
        },
      })
      navigate(`/orders/${result.orderId}`)
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  if (!product) return <p className="muted">{error || '加载中…'}</p>

  const planSkus = skus.filter((sku) => sku.optionId != null)
  const tiers = product.quantityTiers ?? []
  const onShelf = product.status === 3

  return (
    <div style={{ paddingBottom: 72 }}>
      <h1 className="page-title">{product.name}</h1>
      {product.buyNotice && (
        <p className="muted" style={{ marginTop: -10, marginBottom: 12, fontSize: 12 }}>{product.buyNotice}</p>
      )}

      {product.contents && product.contents.length > 0 && <ContentsGrid contents={product.contents} />}

      <div className="card" style={{ display: 'flex', gap: 24, alignItems: 'flex-start', marginTop: 16 }}>
        {product.iconUrl && (
          <img src={product.iconUrl} alt="" style={{ width: 180, height: 180, objectFit: 'contain' }} />
        )}
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', gap: 12, alignItems: 'baseline', flexWrap: 'wrap' }}>
            <div style={{ fontSize: 22, color: 'var(--primary-strong)', fontWeight: 700 }}>
              ¥{(product.finalPriceCents / 100).toFixed(2)}
            </div>
            {product.regularPriceCents > product.finalPriceCents && (
              <span className="muted" style={{ fontSize: 13, textDecoration: 'line-through' }}>
                ¥{(product.regularPriceCents / 100).toFixed(2)}
              </span>
            )}
            {product.promotion && (
              <span className="rarity-tag">促销价 · 库存 {product.promotion.stock}</span>
            )}
          </div>
          <p className="muted" style={{ margin: '4px 0', fontSize: 12 }}>
            支持优惠券与金币抵扣 · 掉落内容见上方
          </p>
          {(planSkus.length > 0 || tiers.length > 0) && (
            <div style={{ margin: '14px 0' }}>
              <div className="muted" style={{ marginBottom: 6 }}>数量套餐</div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <button
                  className={`btn ${selectedSkuId === null && selectedTier === 0 ? '' : 'secondary'}`}
                  onClick={() => selectSku(null, 0)}
                >
                  单个 ¥{(product.finalPriceCents / 100).toFixed(2)}
                </button>
                {planSkus.map((sku) => (
                  <button
                    key={sku.optionId}
                    className={`btn ${selectedSkuId === sku.optionId ? '' : 'secondary'}`}
                    onClick={() => selectSku(sku.optionId, 0)}
                  >
                    {sku.quantity} 个 ¥{(sku.totalPriceCents / 100).toFixed(2)}
                  </button>
                ))}
                {tiers.map((tier) => (
                  <button
                    key={tier.quantity}
                    className={`btn ${selectedTier === tier.quantity ? '' : 'secondary'}`}
                    onClick={() => selectSku(null, tier.quantity)}
                  >
                    {tier.quantity} 个 ¥{((tier.priceCents * tier.quantity) / 100).toFixed(2)}
                  </button>
                ))}
              </div>
            </div>
          )}
          {canUseCoin && (
            <div style={{ margin: '10px 0' }}>
              <label className="muted" style={{ fontSize: 13 }}>金币抵扣</label>
              <select
                value={coins}
                onChange={(event) => setCoins(Number(event.target.value))}
                style={{ marginLeft: 8, padding: '6px 8px', borderRadius: 'var(--radius-s)', border: '1px solid var(--border)' }}
              >
                {COIN_CHOICES.map((choice) => (
                  <option key={choice} value={choice}>
                    {choice === 0 ? '不使用金币' : `抵扣 ${choice} 金币（−¥${(choice / 100).toFixed(2)}）`}
                  </option>
                ))}
              </select>
            </div>
          )}
          {canUseCoupon && (
            <div style={{ margin: '10px 0' }}>
              <label className="muted" style={{ fontSize: 13 }}>优惠券</label>
              <select
                value={couponId}
                onChange={(event) => setCouponId(event.target.value)}
                style={{ marginLeft: 8, padding: '6px 8px', borderRadius: 'var(--radius-s)', border: '1px solid var(--border)', maxWidth: 240 }}
              >
                <option value="">不使用</option>
                {myCoupons.map((coupon) => (
                  <option key={coupon.id} value={coupon.id}>
                    {coupon.title} · 满 {(coupon.minOrderCents / 100).toFixed(0)} 减{' '}
                    {(coupon.amountOffCents ?? 0) > 0
                      ? (coupon.amountOffCents / 100).toFixed(2)
                      : `${coupon.percentOff}%`}
                  </option>
                ))}
              </select>
            </div>
          )}
          {error && <p className="error-text">{error}</p>}
          {chosenCoupon && (
            <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
              已选「{chosenCoupon.title}」，券后约 ¥{(estimatedCents / 100).toFixed(2)}
            </div>
          )}
          <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
            支付后进入开箱流程，结果即时可见；取回到 Steam 待 vapor 联调
          </div>
        </div>
      </div>

      <div
        className="buybar"
        style={{
          position: 'fixed',
          bottom: 0,
          left: '50%',
          transform: 'translateX(-50%)',
          width: '100%',
          maxWidth: 'var(--container)',
          background: 'var(--surface)',
          borderTop: '1px solid var(--border)',
          padding: '12px 24px',
          display: 'flex',
          gap: 16,
          alignItems: 'center',
          zIndex: 10,
        }}
      >
        <div style={{ flex: 1 }}>
          <div className="muted" style={{ fontSize: 12 }}>应付（券后）</div>
          <div style={{ fontSize: 18, color: 'var(--primary-strong)', fontWeight: 700 }}>
            ¥{(estimatedCents / 100).toFixed(2)}
          </div>
        </div>
        <button className="btn" style={{ minWidth: 200 }} onClick={placeOrder} disabled={!onShelf}>
          {onShelf ? '立即开箱' : '已下架'}
        </button>
      </div>
    </div>
  )
}
