import { NavLink, Route, Routes } from 'react-router-dom';
import { StartCrawlPage } from './pages/StartCrawlPage';
import { JobDetailsPage } from './pages/JobDetailsPage';
import { HistoryPage } from './pages/HistoryPage';

// The route table — the equivalent of Angular's `Routes` array plus <router-outlet>.
export function App() {
  return (
    <>
      <header className="header">
        <span className="brand">Web Crawler</span>
        <nav>
          <NavLink to="/" end>New crawl</NavLink>
          <NavLink to="/history">History</NavLink>
        </nav>
      </header>
      <main className="main">
        <Routes>
          <Route path="/" element={<StartCrawlPage />} />
          <Route path="/jobs/:jobId" element={<JobDetailsPage />} />
          <Route path="/history" element={<HistoryPage />} />
          <Route path="*" element={<p>Page not found.</p>} />
        </Routes>
      </main>
    </>
  );
}
