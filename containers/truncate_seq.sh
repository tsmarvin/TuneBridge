#!/usr/bin/env bash
# truncate_seq.sh — Trims the PDS sequencer.sqlite repo_seq table.
#
# Author: Bailey Townsend
# Source: https://tangled.org/strings/did:plc:rnpkyqnmsw4ipey6eotbdnnf/3milxyxx2hl22
# Vendored verbatim into BridgeBeats with attribution; interface unchanged.
#
# Deletes repo_seq rows older than N hours. BridgeBeats invokes it with a
# 14-day (336-hour) retention via the cron entry registered by install.sh
# (see install.sh "Setting Up PDS Sequencer Trim"). Runs directly on the
# host; requires the host sqlite3 package (installed by install.sh).
#
# usage: truncate_seq.sh {db path} {hours back to trim}
#   e.g. truncate_seq.sh /path/to/data/pds/sequencer.sqlite 336
set -euo pipefail
#exec &>> /pds/capture-log.txt
DB_PATH="${1:-sequencer.sqlite}"
HOURS_AGO="${2:-48}"

# Calculate the cutoff timestamp in ISO8601 format
TRUNCATE_FROM=$(date -u -d "${HOURS_AGO} hours ago" +"%Y-%m-%dT%H:%M:%S.000Z" 2>/dev/null \
 || date -u -v-${HOURS_AGO}H +"%Y-%m-%dT%H:%M:%S.000Z") # macOS fallback

echo "Deleting rows from repo_seq where sequencedAt < ${TRUNCATE_FROM}"

sqlite3 \
 -cmd ".timeout 10000" \
 "${DB_PATH}" \
 "PRAGMA journal_mode=WAL;
 PRAGMA busy_timeout=10000;
 DELETE FROM repo_seq WHERE sequencedAt < '${TRUNCATE_FROM}';"

echo "Done."
