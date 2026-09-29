-- Idempotent migration after Redis backplane rollout.
-- Speed up connection registry queries used on hub join and targeted fanout.

CREATE INDEX IF NOT EXISTS idx_bff_store_conn_user
    ON bff.store ((value->>'userId'))
    WHERE namespace = 'conn';

CREATE INDEX IF NOT EXISTS idx_bff_store_conn_game
    ON bff.store ((value->>'gameId'))
    WHERE namespace = 'conn';
