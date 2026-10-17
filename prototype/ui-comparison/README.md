# UI shell comparison (Stage 3, pending)

Svelte is the provisional Stage 1 shell in `web/`; no selection has been finalized.
Implement the small plain TypeScript alternative here against the same generated
contracts, `web/src/lib/session-client.ts`, read-only fixture and eventual renderer.
Do not copy transport or Canvas implementations into the experiment.

Compare toolbar, train inspector, Japanese station-name input, and scrollable
building catalog; use identical delayed/rejected command behavior. Record adding
a second inspector, state synchronization, reconnect subscription cleanup,
loading/errors, keyboard/IME behavior, localization/layout/accessibility, and
build/startup size. Choose one shell before implementing the full application.
