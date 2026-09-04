\connect wordgamebff

CREATE SCHEMA IF NOT EXISTS bff;

CREATE TABLE IF NOT EXISTS bff.store (
    namespace   TEXT        NOT NULL,
    key         TEXT        NOT NULL,
    value       JSONB       NOT NULL,
    expires_at  TIMESTAMPTZ,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (namespace, key)
);

CREATE INDEX IF NOT EXISTS idx_bff_store_expires
    ON bff.store (expires_at)
    WHERE expires_at IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_bff_store_conn_user
    ON bff.store ((value->>'userId'))
    WHERE namespace = 'conn';

CREATE INDEX IF NOT EXISTS idx_bff_store_conn_game
    ON bff.store ((value->>'gameId'))
    WHERE namespace = 'conn';

CREATE TABLE IF NOT EXISTS bff.game_revisions (
    game_id   BIGINT PRIMARY KEY,
    revision  BIGINT NOT NULL DEFAULT 0
);
