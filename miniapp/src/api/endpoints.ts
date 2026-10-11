import Taro from '@tarojs/taro'
import { api, clearTokens, saveTokens } from './client'

interface TokenPair {
  userId: string
  nickname: string
  accessToken: string
  refreshToken: string
}

/** wx.login → POST /api/auth/wechat → JWT pair stored locally. */
export async function wechatLogin(nickname?: string): Promise<TokenPair> {
  const { code } = await Taro.login()
  const tokens = await api<TokenPair>('/api/auth/wechat', {
    method: 'POST',
    data: { code, nickname },
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
  const refreshToken = Taro.getStorageSync<string>('doggo.refreshToken')
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

export function fetchProducts(page = 1) {
  return api<{ total: number; items: ProductView[] }>(`/api/products?page=${page}&pageSize=20`)
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
  status: number
  totalCostCents: number
}

/** Case entry, self-open, spot, WeChat pay — the standard single-open. */
export function createOrder(userId: string, productId: string, planId?: string, optionId?: string) {
  return api<OrderView>('/api/orders', {
    method: 'POST',
    data: {
      userId,
      productId,
      planId: planId ?? null,
      optionId: optionId ?? null,
      entry: 1,
      serviceMode: 0,
      fulfillmentType: 1,
      payType: 2
    }
  })
}

export function payOrder(id: string) {
  return api<{ paid: boolean }>(`/api/orders/${id}/pay`, { method: 'POST' })
}

export function cancelOrder(id: string) {
  return api<{ cancelled: boolean }>(`/api/orders/${id}/cancel`, { method: 'POST' })
}

export interface SeatView {
  seatNo: number
  status: number
  crateAssetId?: string | null
  keyAssetId?: string | null
  prizeAssetId?: string | null
  orderId?: string | null
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
