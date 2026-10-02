-- Phase 4 deliberately uses one bounded document and one transaction lock.
-- This is a local control plane, not a horizontally scaled game authority.
CREATE TABLE IF NOT EXISTS enfractal_meta (
    singleton smallint PRIMARY KEY CHECK (singleton = 1),
    version integer NOT NULL CHECK (version = 1),
    host_fence text NOT NULL DEFAULT ''
);
INSERT INTO enfractal_meta(singleton,version) VALUES (1, 1) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS enfractal_state (
    singleton smallint PRIMARY KEY CHECK (singleton = 1),
    document jsonb NOT NULL,
    sha256 text NOT NULL CHECK (length(sha256) = 64)
);
