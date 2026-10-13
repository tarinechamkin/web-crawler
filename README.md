# Web Crawler

> Work in progress — remaining sections (how to run, architecture, messaging, decisions) to follow.

## Known limitations

- **Start URL that redirects to another host.** The "starting domain" is the host of the URL the user typed
  (per the spec). If that URL redirects to a different host — e.g. `example.com` → `www.example.com` — every link
  on the landing page counts as external: the crawl stops after the first page and ratios are skewed toward 0.
  *Possible fix:* after fetching the root page, adopt the final (post-redirect) host as the starting domain, and
  record that choice on the job so it is visible in the UI.
