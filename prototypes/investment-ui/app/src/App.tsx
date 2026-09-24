import { Routes, Route } from 'react-router-dom'
import PortfolioPage from './pages/PortfolioPage'
import FinancialsPage from './pages/FinancialsPage'

function App() {
  return (
    <Routes>
      <Route path="/" element={<PortfolioPage />} />
      <Route path="/financials" element={<FinancialsPage />} />
    </Routes>
  )
}

export default App
