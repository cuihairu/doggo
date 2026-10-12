import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import App from './App'
import NewsListPage from './pages/NewsListPage'
import NewsDetailPage from './pages/NewsDetailPage'
import ArticlePage from './pages/ArticlePage'
import MarketPage from './pages/MarketPage'
import MarketDetailPage from './pages/MarketDetailPage'
import ProductListPage from './pages/ProductListPage'
import ProductDetailPage from './pages/ProductDetailPage'
import OrderPayPage from './pages/OrderPayPage'
import CaseOpeningPage from './pages/CaseOpeningPage'
import MyPrizesPage from './pages/MyPrizesPage'
import RollRoomsPage from './pages/RollRoomsPage'
import UserCenterPage from './pages/UserCenterPage'
import LoginPage from './pages/LoginPage'
import './theme.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route element={<App />}>
          <Route path="/" element={<NewsListPage />} />
          <Route path="/news/:id" element={<NewsDetailPage />} />
          <Route path="/articles/:id" element={<ArticlePage />} />
          <Route path="/market" element={<MarketPage />} />
          <Route path="/market/:id" element={<MarketDetailPage />} />
          <Route path="/products" element={<ProductListPage />} />
          <Route path="/products/:id" element={<ProductDetailPage />} />
          <Route path="/orders/:id" element={<OrderPayPage />} />
          <Route path="/case-openings/:batchId" element={<CaseOpeningPage />} />
          <Route path="/prizes" element={<MyPrizesPage />} />
          <Route path="/roll" element={<RollRoomsPage />} />
          <Route path="/user" element={<UserCenterPage />} />
          <Route path="/login" element={<LoginPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  </StrictMode>,
)
