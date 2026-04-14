-- 018_CreateExpertSessionTables.sql
-- Adds conversation persistence for the Expert (VinSomm-eksperten) feature.
-- Three new tables: expert_sessions, expert_messages, expert_wine_suggestions.
-- Enables users to revisit past conversations and give feedback on wine suggestions.

-- ── 1. expert_sessions ──────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS expert_sessions (
    id          UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID          NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    title       TEXT,
    created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_expert_sessions_user_updated
    ON expert_sessions (user_id, updated_at DESC);

ALTER TABLE expert_sessions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users manage own expert sessions" ON expert_sessions;
CREATE POLICY "Users manage own expert sessions" ON expert_sessions
    FOR ALL
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);

-- ── 2. expert_messages ──────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS expert_messages (
    id          UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id  UUID          NOT NULL REFERENCES expert_sessions(id) ON DELETE CASCADE,
    role        TEXT          NOT NULL CHECK (role IN ('user', 'assistant')),
    content     TEXT          NOT NULL,
    model_used  TEXT,
    created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_expert_messages_session_created
    ON expert_messages (session_id, created_at ASC);

ALTER TABLE expert_messages ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users manage own expert messages" ON expert_messages;
CREATE POLICY "Users manage own expert messages" ON expert_messages
    FOR ALL
    USING (
        session_id IN (SELECT id FROM expert_sessions WHERE user_id = auth.uid())
    )
    WITH CHECK (
        session_id IN (SELECT id FROM expert_sessions WHERE user_id = auth.uid())
    );

-- ── 3. expert_wine_suggestions ──────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS expert_wine_suggestions (
    id          UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id  UUID          NOT NULL REFERENCES expert_messages(id) ON DELETE CASCADE,
    wine_id     UUID          REFERENCES wines(id) ON DELETE SET NULL,
    wine_data   JSONB         NOT NULL,
    feedback    SMALLINT,
    created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_expert_wine_suggestions_message
    ON expert_wine_suggestions (message_id);

CREATE INDEX IF NOT EXISTS idx_expert_wine_suggestions_feedback
    ON expert_wine_suggestions (feedback) WHERE feedback IS NOT NULL;

ALTER TABLE expert_wine_suggestions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users manage own expert wine suggestions" ON expert_wine_suggestions;
CREATE POLICY "Users manage own expert wine suggestions" ON expert_wine_suggestions
    FOR ALL
    USING (
        message_id IN (
            SELECT em.id FROM expert_messages em
            JOIN expert_sessions es ON es.id = em.session_id
            WHERE es.user_id = auth.uid()
        )
    )
    WITH CHECK (
        message_id IN (
            SELECT em.id FROM expert_messages em
            JOIN expert_sessions es ON es.id = em.session_id
            WHERE es.user_id = auth.uid()
        )
    );
