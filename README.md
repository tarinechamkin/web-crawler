# Web Crawler

> Work in progress — remaining sections (how to run, architecture, messaging, decisions) to follow.

## Known limitations

- **Start URL that redirects to another host.** The "starting domain" is the host of the URL the user typed
  (per the spec). If that URL redirects to a different host — e.g. `example.com` → `www.example.com` — every link
  on the landing page counts as external: the crawl stops after the first page and ratios are skewed toward 0.
  *Possible fix:* after fetching the root page, adopt the final (post-redirect) host as the starting domain, and
  record that choice on the job so it is visible in the UI.
- **HTTPS → HTTP redirects are not followed.** .NET refuses to downgrade a secure request, and so do we: such
  pages are recorded with the error "Redirect not followed: HTTPS to HTTP downgrade (target)". Example:
  `quotes.toscrape.com/author/...` redirects to `http://...`. Following them would mean silently giving up
  transport security for a misconfigured site.
- **robots.txt is not honored**, and there is no per-host rate limit beyond fetching one page at a time.
  A production crawler must do both. Only crawl sites you own or that invite it (`*.toscrape.com`, `crawler-test.com`).
- **A job waiting for a message-level retry still shows "Running"** (it can sit for up to 2 minutes between
  attempts), which looks frozen. *Possible improvement:* store the attempt number and next-retry time on the job
  and show "Retrying (attempt 2 of 3)" in the UI.
- **No worker running → jobs wait in Pending.** Nothing is lost (the message waits in RabbitMQ and is processed
  as soon as a worker starts), but the UI cannot tell the user why. *Possible improvement:* show "waiting for a
  worker" when a job has been Pending for more than a few seconds, and alert on queue depth.
