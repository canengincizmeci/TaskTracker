# Production deployment checklist

The repository README describes an Ubuntu deployment with host Nginx, systemd and PostgreSQL. Keep that topology if it is already in use. `compose.production.yaml` is an optional single-host deployment for the same API and frontend. Use the two Compose files independently. The default `compose.yaml` is for local development.

## 1. Prepare the host and credentials

- Verify the public DNS name, the certificate and its renewal, the host firewall, and the actual Nginx-to-upstream route. Expose HTTPS and the HTTP redirect only. Bind a systemd API to loopback and trust only the exact IP of its Nginx peer. Never publish PostgreSQL or the ClamAV socket. Restrict administration to approved channels.
- Copy `.env.production.example` to a host file outside the checkout such as `/etc/tasktracker/compose.env`. Replace the example hostname, mail settings, paths and network addresses. Choose a free edge subnet. The published frontend port is loopback-only. Use a supported Docker Engine release and confirm externally that the port is unreachable. The migration image currently targets Linux amd64.
- Provision credentials outside the repository. Give the application and migrator service users access only to their respective directories and files. For the container images, verify the effective API/migrator UID before setting ownership. Owner-only directory `0700` and file `0400` are suitable when the process owns them. Use separate strong generated values for signing and OTP HMAC, with at least 32 random bytes each. Keep existing keys during upgrades unless intentionally rotating them.
- Put `ConnectionStrings__PostgreSql`, `TokenOptions__SecurityKey`, `PasswordRecovery__HmacSecret`, `Smtp__User` and `Smtp__Pass` in separate API files. Put only `ConnectionStrings__PostgreSql` in the migration directory. Use the dedicated DB login for each connection string and valid Npgsql quoting for passwords with delimiters. The PostgreSQL bootstrap password file is separate and applies to fresh initialization. Changing that file does not rotate a password in an existing PostgreSQL cluster. Avoid secret-bearing command arguments, shell history and debug logs.
- For container deployment, put two files in `TASKTRACKER_PROXY_TRUST_DIR`. In `real-ip.conf`, put `set_real_ip_from <exact-host-peer-address>;`. In `https.map`, put `<exact-host-peer-address>:https https;`. The address is the peer the frontend container actually sees from host Nginx. A bridge gateway is common. Verify it on this host before trusting it. The API independently trusts the frontend container's exact `TASKTRACKER_CLIENT_PROXY_IP`. The host proxy must replace incoming forwarded headers. If the host sits behind a load balancer, configure and verify that separate trust boundary before enabling forwarded IP attribution.
- Use `nginx-edge.conf.example` only after filling in the verified hostname, local port and certificate paths. It assumes the container frontend upstream. For systemd, adapt existing API and hub upstreams and keep the raw query out of access/error log sinks. `nginx -t` must pass before reloading. Check `https` redirects, HSTS, unique client IP attribution and both WebSocket hubs in staging. Add CSP in report-only mode first if the site needs it. The example does not provision TLS certificates or a default virtual host.

## 2. Bootstrap or review database access

Start only PostgreSQL and ClamAV for a fresh container installation. Use a dedicated application database. As its administrator, create a migration login and a distinct runtime login. Use interactive `psql` `\password` or the host's secret manager so credentials stay out of SQL history. The role names below are examples. Review existing grants and object ownership before changing a populated database.

```sql
CREATE ROLE tasktracker_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE ROLE tasktracker_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
REVOKE CONNECT, TEMPORARY ON DATABASE tasktracker FROM PUBLIC;
GRANT CONNECT ON DATABASE tasktracker TO tasktracker_migrator, tasktracker_runtime;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO tasktracker_migrator;
GRANT USAGE ON SCHEMA public TO tasktracker_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO tasktracker_runtime;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA public TO tasktracker_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE tasktracker_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tasktracker_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE tasktracker_migrator IN SCHEMA public
    GRANT USAGE ON SEQUENCES TO tasktracker_runtime;
```

For an existing database, have the DBA arrange owner privileges on the specific EF-managed objects for the migrator, including migration history. Retain any required monitoring and backup login grants. After the first migration, remove the runtime role's privileges on `public."__EFMigrationsHistory"`. Verify access with the runtime login and confirm that DDL is denied. Keep the host port unpublished and use SCRAM and restrictive `pg_hba.conf` rules where applicable. For PostgreSQL over a separate network, configure Npgsql `SSL Mode=VerifyFull` and a trusted CA. Never print or pass the URL with inline credentials on a command line.

## 3. Validate and release

For the optional Compose deployment, use a stable project name and keep the same volumes across releases. A different project name creates a different set of volumes.

```sh
tt() { docker compose --project-name tasktracker-prod --env-file /etc/tasktracker/compose.env -f compose.production.yaml "$@"; }
tt --profile migration config --quiet
tt --profile migration build
tt up -d db clamav
tt ps
```

1. Review CVEs and set the approved digests using `TASKTRACKER_POSTGRES_IMAGE`, `TASKTRACKER_CLAMAV_IMAGE`, `TASKTRACKER_DOTNET_SDK_IMAGE`, `TASKTRACKER_DOTNET_RUNTIME_IMAGE`, `TASKTRACKER_NODE_BUILD_IMAGE` and `TASKTRACKER_NGINX_RUNTIME_IMAGE`. Keep PostgreSQL in the same major version and compatible OS/collation family for an existing volume. Scan the built images, including OS packages added by the Dockerfiles.
2. Rehearse the migration against a restored copy. Take consistent backups of the database and attachment storage. For an incompatible schema, stop old API writers before executing `tt run --rm migrate`. Check the exit code, then run `tt up -d --no-deps --force-recreate --wait api` and `tt up -d --no-deps --force-recreate --wait client` in order. Inspect `tt ps -a`. Batch 04 deliberately invalidates plaintext refresh sessions if that migration is pending. Batch 05 adds no schema migration.
3. Verify `curl --head http://<hostname>/` and `curl --fail --head https://<hostname>/`. Verify client liveness locally with `curl --fail http://127.0.0.1:<port>/_health`. The existing API `/health` is a liveness check. Independently verify a database-backed request, login/refresh/logout, email delivery, both SignalR hubs and an attachment upload/download with a staging account. Verify that access/error logs omit the query, credentials and token values, including for proxy errors. Use staging addresses to verify that a forged forwarding header does not change the effective client IP.
4. Check ClamAV health and signature freshness separately. `tt exec -T clamav /usr/local/bin/clamdcheck.sh` checks scanner health. Review FreshClam update logs or query `VERSION` from the daemon on its private network. Test a clean file and the official EICAR test file through an authenticated staging upload using an allowed file extension. Verify rejection of EICAR, rejection when the scanner is unavailable, and the absence of attachment rows and orphan blobs for both failures. Restore scanner connectivity and repeat the clean upload. Use only staging data and keep FreshClam's outbound signature access available.
5. Monitor API latency, HTTP 429 and 5xx rates, container OOMs, database and attachment disk use, signature freshness and certificate renewal. Set host storage quotas or alerts for durable data. The API and frontend containers have read-only roots and bounded temp storage. The migration container keeps a private executable tmpfs for its self-contained runtime extraction. Keep the standard .NET image's app UID of `1654` when swapping base images. Size the host and tune the memory budgets for upload demand and ClamAV signature reloads.

## 4. Back up, restore and roll back

- Save a PostgreSQL custom-format dump and an archive of the API attachment directory to encrypted off-host storage with restricted access. Stop or otherwise quiesce API writers while taking both copies so the database metadata matches the blobs. Record the release, schema migration, image digests and backup generation together. Keep certificate and secret recovery separate from the application archive.
- Example commands with writers paused. Use a protected, pre-created backup directory. Replace the role and database placeholders and use a credential file or prompt if required by the local DBA.

  ```sh
  umask 077
  tt exec -T db pg_dump -U <backup-role> -d <database> --format=custom > "$backup/database.dump"
  api_container=$(tt ps --all --quiet api)
  docker cp "$api_container:/app/App_Data/task-files/." - > "$backup/task-files.tar"
  pg_restore --list "$backup/database.dump" >/dev/null
  tar --list --file "$backup/task-files.tar" >/dev/null
  ```

- Restore into an isolated instance using a compatible `pg_restore` and an empty database. Create roles and grants first, including `USAGE, CREATE` on the target schema for the migration role. Use `pg_restore --exit-on-error --single-transaction --no-owner --no-privileges --role=tasktracker_migrator --dbname=<isolated-database> "$backup/database.dump"` with a restore login permitted to assume that role. Restore the matching attachment archive into an empty staging volume, set the API UID's ownership and verify samples through authenticated download. Check object counts, migration history and permissions. Test restore on a schedule.
- For an app-only rollback, use the previous tested image only if it works with the current schema. For an incompatible schema, keep writers stopped and use the tested rollback migration or restore the paired backup to a separate volume and switch over after validation. Plan explicitly for writes made after the backup. Preserve the current volume for recovery. Previous refresh sessions cannot be re-enabled after the Batch 04 credential cutover. Before moving from an older Compose deployment, copy attachments out of the old API container's writable layer before replacing it. For systemd, include the configured `TaskFiles:RootPath` in the backup and restore separately.

## Release checks and remaining controls

- Run `dotnet list TaskTracker.slnx package --vulnerable --include-transitive`, `npm audit`, and an image scanner such as `docker scout cves <exact-image>` against your release. Review advisory applicability and remediate or record an owner and deadline before release. Repeat scanning after base-image updates, and use CI status checks as a merge prerequisite if your repository policy permits it.
- The 2026-11-26 Docker Scout `linux/amd64` scan reported the following unresolved base or release-image findings. These counts describe the scanned digests and do not establish application reachability. Re-scan the digests selected for rollout.

  | Image | Scan digest prefix | Critical | High |
  | --- | --- | ---: | ---: |
  | `postgres:16` | `e8a7960781a4` | 4 | 35 |
  | Built frontend with unprivileged Nginx Alpine slim | `d79eb59a1589` | 6 | 7 |
  | `clamav/clamav:1.4` | `ae2405b10591` | 0 | 1 |
  | `node:24-bookworm-slim` (build only) | `e0e989b967da` | 0 | 13 |

- Scan tracked working files without emitting values. For a historical check, use a disposable, access-controlled checkout with all relevant refs and run `gitleaks git --redact=100 <checkout-path>`. Restrict reports and rotate credentials if a leak is confirmed. Review build histories, backup access and any outer proxy or telemetry separately.

[PostgreSQL privileges](https://www.postgresql.org/docs/16/ddl-priv.html) · [PostgreSQL backup and restore](https://www.postgresql.org/docs/16/backup-dump.html) · [ClamAV Docker operation](https://docs.clamav.net/manual/Installing/Docker.html) · [Gitleaks](https://github.com/gitleaks/gitleaks)
