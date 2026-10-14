import { api, clearTokens, getRefreshToken, saveTokens } from './client'

interface TokenPair {
  userId: string
  nickname: string
  accessToken: string
  refreshToken: string
}

/** Account register: POST /api/auth/register → JWT pair stored locally. */
export async function accountRegister(account: string, password: string, nickname?: string, inviteCode?: string): Promise<TokenPair> {
  const tokens = await api<TokenPair>('/api/auth/register', {
    method: 'POST',
    data: { account, password, nickname, inviteCode },
    skipAuth: true
  })
  saveTokens(tokens.accessToken, tokens.refreshToken)
  return tokens
}

/** Account login: POST /api/auth/login → JWT pair stored locally. */
export async function accountLogin(account: string, password: string): Promise<TokenPair> {
  const tokens = await api<TokenPair>('/api/auth/login', {
    method: 'POST',
    data: { account, password },
    skipAuth: true
  })
  saveTokens(tokens.accessToken, tokens.refreshToken)
  return tokens
}

export interface Profile {
  id: string
  phone: string
  email: string
  nickname: string
  avatarUrl: string
  inviteCode: string
  status: string
}

export function fetchProfile() {
  return api<Profile>('/api/auth/me')
}

export function logout() {
  const refreshToken = getRefreshToken()
  if (refreshToken) {
    api('/api/auth/logout', { method: 'POST', data: { refreshToken } }).catch(() => {})
  }
  clearTokens()
}

export interface NewsItemView {
  id: string
  title: string
  game: number
  category: string
  tags: string
  summary?: string | null
  coverUrl?: string | null
  source: string
  publishedAtUtc: string
  viewCount: number
}

export function fetchNews(page = 1) {
  return api<{ total: number; items: NewsItemView[] }>(`/api/news?page=${page}&pageSize=20`)
}

export interface MarketItemView {
  id: string
  classId: string
  marketHashName: string
  itemType: number
  game: number
  iconUrl: string
  priceCents?: number | null
  currency?: string | null
}

export function fetchMarket(page = 1) {
  return api<{ total: number; items: MarketItemView[] }>(`/api/market/items?page=${page}&pageSize=20`)
}

export interface PricePoint {
  date: string
  openCents: number
  highCents: number
  lowCents: number
  closeCents: number
}

export function fetchMarketPrices(id: string) {
  return api<{ item: string; points: PricePoint[] }>(`/api/market/items/${id}/prices?days=30`)
}

export interface ProductView {
  id: string
  type: number
  name: string
  classId: string
  iconUrl: string
  status: number
  finalPriceCents: number
  regularPriceCents: number
  sortOrder: number
}

export function fetchProducts(page = 1, filters?: { q?: string }) {
  const params = new URLSearchParams({ page: String(page), pageSize: '20' })
  const q = filters?.q?.trim()
  if (q) params.set('q', q)
  return api<{ total: number; items: ProductView[] }>(`/api/products?${params}`)
}

export interface ProductDetail extends ProductView {
  plans: {
    id: string
    title: string
    minQuantity: number
    maxQuantity: number
    canUseCoupon: boolean
    canUseCoin: boolean
    cashbackBps: number
    options: { id: string; quantity: number; priceCents: number; upliftBps: number }[]
  }[]
  contents: { id: string; title: string; quality: number; color: string; imageUrl: string; referencePriceCents: number }[]
  quantityTiers: { id: string; quantity: number; priceCents: number }[]
}

export function fetchProduct(id: string) {
  return api<ProductDetail>(`/api/products/${id}`)
}

export interface OrderView {
  id: string
  orderNo: string
  userId: string
  productId: string
  /** Joined on GET /api/orders/{id}; empty when the product row is gone. */
  productName: string
  productIconUrl?: string
  planId?: string | null
  planTitle?: string | null
  status: number
  regularPriceCents: number
  totalPriceCents: number
  totalCostCents: number
  coinDeduction: number
  couponDeductionCents: number
  cashbackCents: number
  error?: string | null
  paidAtUtc?: string | null
  payDeadlineUtc?: string | null
  completedAtUtc?: string | null
  createdAtUtc: string
}

/** Case entry, self-open, spot, WeChat pay — the standard single-open.
 * userCouponId selects a UserCoupon (the /api/coupons/mine row id) whose
 * discount the server applies and validates authoritatively. quantity only
 * applies to plain buys without a plan option; the server prices it through
 * the product's quantity-tier ladder. */
export function createOrder(userId: string, productId: string, planId?: string, optionId?: string, userCouponId?: string, quantity = 1) {
  // The create endpoint returns { orderId, ... } while the detail endpoint
  // returns { id, ... }; normalize so callers can use order.id either way.
  return api<OrderView & { orderId?: string }>('/api/orders', {
    method: 'POST',
    data: {
      userId,
      productId,
      planId: planId ?? null,
      optionId: optionId ?? null,
      entry: 1,
      serviceMode: 0,
      fulfillmentType: 1,
      payType: 2,
      userCouponId: userCouponId || null,
      quantity: Math.max(Math.trunc(quantity) || 1, 1)
    }
  }).then((order) => ({ ...order, id: order.orderId ?? order.id }))
}

export function fetchOrder(id: string) {
  return api<OrderView>(`/api/orders/${id}`)
}

export function payOrder(id: string) {
  return api<{ paid: boolean }>(`/api/orders/${id}/pay`, { method: 'POST' })
}

export function cancelOrder(id: string) {
  return api<{ cancelled: boolean }>(`/api/orders/${id}/cancel`, { method: 'POST' })
}

/** Prize attached to a seat once the opening result landed on its order. */
export interface SeatPrizeView {
  assetId: string
  classId: string
  instanceId: string
  marketHashName: string
  quality: number
  exterior: string
  floatValue?: number | null
  rarity: string
  iconUrl: string
  inspectLink?: string | null
  priceCents: number
  tradableAtUtc?: string | null
}

export interface SeatView {
  seatNo: number
  status: number
  crateAssetId?: string | null
  keyAssetId?: string | null
  prizeAssetId?: string | null
  orderId?: string | null
  prize?: SeatPrizeView | null
}

export function fetchBatch(batchId: string) {
  return api<{ id: string; status: number; seats: SeatView[] }>(`/api/group-buyings/${batchId}`)
}

export interface PrizeView {
  id: string
  orderId: string
  marketHashName: string
  quality: number
  exterior: string
  rarity: string
  iconUrl: string
  priceCents: number
  tradableAtUtc?: string | null
  marketPriceCents?: number | null
}

export function fetchMyPrizes(userId: string) {
  return api<{ items: PrizeView[]; page: number; pageSize: number; total: number }>(`/api/users/${userId}/prizes`)
}

export function fetchCoinBalance() {
  return api<{ balance: number; checkedAtUtc: string }>('/api/coins/balance')
}

export interface CoinLogEntry {
  id: string
  type: number
  delta: number
  balanceAfter: number
  note?: string | null
  createdAtUtc: string
}

/** The caller's own coin ledger, newest first. */
export function fetchCoinLogs(page = 1, pageSize = 10) {
  return api<{ total: number; page: number; pageSize: number; items: CoinLogEntry[] }>(
    `/api/coins/logs?page=${page}&pageSize=${pageSize}`,
  )
}

export interface MyCoupon {
  id: string
  couponId: string
  title: string
  type: number
  amountOffCents: number
  percentOff: number
  minOrderCents: number
  status: number
  validToUtc?: string | null
}

export function fetchMyCoupons() {
  return api<MyCoupon[]>('/api/coupons/mine')
}

export interface ClaimableCoupon {
  id: string
  title: string
  type: number
  amountOffCents: number
  percentOff: number
  minOrderCents: number
  perUserLimit: number
  totalLimit?: number | null
  validFromUtc?: string | null
  validToUtc?: string | null
}

/** Coupon center: every enabled coupon inside its claim window. */
export function fetchClaimableCoupons() {
  return api<ClaimableCoupon[]>('/api/coupons')
}

/** Claim a coupon; the server enforces the per-user and total limits. */
export function claimCoupon(couponId: string) {
  return api<MyCoupon>(`/api/coupons/${couponId}/claim`, { method: 'POST' })
}

export interface RedeemResult {
  type: number
  coinAmount: number
  couponId?: string | null
  couponTitle: string
  balance: number
}

/** Redeem code exchange: POST /api/redeem → coin credit or coupon grant. */
export function redeemCode(code: string) {
  return api<RedeemResult>('/api/redeem', { method: 'POST', data: { code } })
}

export interface TradeUrlEntry {
  id: string
  steamId: string
  url: string
  nickname: string
  isPreferred: boolean
  status: number
  checkError?: string | null
  checkedAtUtc?: string | null
}

/** Steam trade links of the user; status 1 = valid. */
export function fetchTradeUrls(userId: string) {
  return api<TradeUrlEntry[]>(`/api/users/${userId}/trade-urls`)
}

export function addTradeUrl(userId: string, url: string, nickname?: string) {
  return api<TradeUrlEntry>(`/api/users/${userId}/trade-urls`, {
    method: 'POST',
    data: { url, nickname: nickname ?? null }
  })
}

export interface SignCycleDay {
  date: string
  signed: boolean
  coinReward: number
  makeupOrderNo?: string | null
}

export function fetchSignCycle() {
  return api<{ cycleId: string; days: SignCycleDay[]; todaySigned: boolean; totalReward: number }>('/api/sign')
}

export function signToday() {
  return api<{ cycleId: string; signedDate: string; coinReward: number; cycleTotalReward: number }>('/api/sign', { method: 'POST' })
}

export interface NoticeView {
  id: string
  title: string
  content: string
}

/** Operator notices currently in their published window. */
export function fetchNotices() {
  return api<NoticeView[]>('/api/notices')
}

export interface InviteRewardView {
  id: string
  inviteeNickname: string
  coinReward: number
  createdAtUtc: string
}

/** Invite rewards the caller has earned, newest first. */
export function fetchInviteRewards(page = 1, pageSize = 10) {
  return api<{ total: number; totalReward: number; page: number; pageSize: number; items: InviteRewardView[] }>(
    `/api/invites?page=${page}&pageSize=${pageSize}`,
  )
}
