# Wi-Fi-only transfers

Enable **Wi-Fi only** in Settings to pause downloads and seeding whenever the network no longer qualifies as Wi-Fi with internet access. It is off by default, including after upgrading an existing installation.

Affected torrents display **Waiting for Wi-Fi** and resume automatically when Wi-Fi returns. Pause or Stop cancels automatic resume for that torrent. Turning the setting off releases waiting torrents to use the current connection. Downloads and seeds retain their existing queue and speed limits.

The waiting state is saved, so it survives an app restart. Manually paused, stopped, completed, and failed items are not automatically started. Previously active items restored after an app shutdown still follow the existing paused-on-restore behavior. Android background execution limits continue to apply; waiting items can resume when the app is allowed to run again.

## Implementation

- `ITransferNetworkMonitor` separates operating-system connectivity from the Core torrent service. Android uses default-network callbacks and validated Wi-Fi capabilities (Android 6 uses the MAUI notification fallback). Windows checks the current internet connection profile. Apple targets use MAUI connectivity.
- Network changes are serialized. Before suspending, the service drains in-flight starts using the existing engine-rebuild barrier, closes managers and the engine, and records waiting states. Closing the engine also closes its discovery sockets.
- Automatic resume rechecks the expected waiting state under each torrent's operation lock, so a manual pause, stop, or removal wins over a stale resume request. Proxy rebuilds and queued starts use the same network gate.
- The policy responds to OS notifications; it does not bind sockets to an interface or provide a packet-level firewall. OS notification and engine teardown latency can allow in-flight traffic during a network handover. A Wi-Fi hotspot may itself use mobile data, and wired connections do not qualify as Wi-Fi.

## Verification

Automated Core tests cover start prevention, event-driven pause/resume, engine teardown, manual overrides, reconnect races, repeated network changes, queue limits, settings persistence, restart restoration, proxy changes, background suspension, and real seeding progress/time preservation.

Before a store release, verify on physical Android and Windows devices: start a download and a seed, leave Wi-Fi, confirm both wait, reconnect and confirm they resume, then repeat after manually pausing one item. Also check a Wi-Fi network without internet, cellular plus secondary Wi-Fi, a wired connection, and a VPN whose underlying transport changes.
