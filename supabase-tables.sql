-- Winternet Quiz - Additional Tables for Test System and Question Difficulty
-- Normalized to lowercase column names

-- Table: test_sessions
-- Stores information about each test session
CREATE TABLE IF NOT EXISTS test_sessions (
    token TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    startedutc TIMESTAMP WITH TIME ZONE NOT NULL,
    completedutc TIMESTAMP WITH TIME ZONE,
    status TEXT NOT NULL CHECK (status IN ('active', 'completed', 'expired')),
    questionsjson TEXT NOT NULL,
    answersjson TEXT NOT NULL DEFAULT '[]',
    currentindex INTEGER NOT NULL DEFAULT 0,
    score INTEGER NOT NULL DEFAULT 0,
    maxscore INTEGER NOT NULL DEFAULT 0,
    createdat TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updatedat TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Indexes for faster queries by username and status
CREATE INDEX IF NOT EXISTS idx_test_sessions_username ON test_sessions(username);
CREATE INDEX IF NOT EXISTS idx_test_sessions_status ON test_sessions(status);
CREATE INDEX IF NOT EXISTS idx_test_sessions_started ON test_sessions(startedutc DESC);

-- Table: question_difficulties
-- Tracks difficulty level and statistics for each question
CREATE TABLE IF NOT EXISTS question_difficulties (
    questionfile TEXT PRIMARY KEY,
    difficulty TEXT NOT NULL CHECK (difficulty IN ('easy', 'medium', 'hard')),
    successrate DECIMAL(5,2) NOT NULL DEFAULT 0,
    totalattempts INTEGER NOT NULL DEFAULT 0,
    correctattempts INTEGER NOT NULL DEFAULT 0,
    manualoverride BOOLEAN NOT NULL DEFAULT FALSE,
    lastupdated TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    createdat TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Indexes for faster queries by difficulty
CREATE INDEX IF NOT EXISTS idx_question_difficulties_difficulty ON question_difficulties(difficulty);
CREATE INDEX IF NOT EXISTS idx_question_difficulties_success_rate ON question_difficulties(successrate);
CREATE INDEX IF NOT EXISTS idx_question_difficulties_last_updated ON question_difficulties(lastupdated DESC);

-- Function to automatically recalculate all difficulties based on success rate
-- Rules: easy >= 70%, medium 40-70%, hard < 40%, questions with 0 attempts remain 'unrated'
CREATE OR REPLACE FUNCTION recalculate_all_difficulties()
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE question_difficulties
    SET 
        difficulty = CASE
            WHEN totalattempts = 0 THEN 'unrated'
            WHEN successrate >= 70 THEN 'easy'
            WHEN successrate >= 40 THEN 'medium'
            ELSE 'hard'
        END,
        lastupdated = CURRENT_TIMESTAMP
    WHERE manualoverride = FALSE;
    
    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$;

-- Trigger to automatically update the 'updatedat' field in test_sessions
CREATE OR REPLACE FUNCTION update_test_sessions_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updatedat = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trigger_update_test_sessions_timestamp ON test_sessions;
CREATE TRIGGER trigger_update_test_sessions_timestamp
    BEFORE UPDATE ON test_sessions
    FOR EACH ROW
    EXECUTE FUNCTION update_test_sessions_timestamp();

-- Trigger to automatically update the 'lastupdated' field in question_difficulties
CREATE OR REPLACE FUNCTION update_question_difficulties_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.lastupdated = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trigger_update_question_difficulties_timestamp ON question_difficulties;
CREATE TRIGGER trigger_update_question_difficulties_timestamp
    BEFORE UPDATE ON question_difficulties
    FOR EACH ROW
    EXECUTE FUNCTION update_question_difficulties_timestamp();

-- Enable Row Level Security
ALTER TABLE test_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE question_difficulties ENABLE ROW LEVEL SECURITY;
ALTER TABLE question_explanations ENABLE ROW LEVEL SECURITY;

-- Drop existing policies if they exist (to avoid duplicates on re-run)
DROP POLICY IF EXISTS "Service role full access to test_sessions" ON test_sessions;
DROP POLICY IF EXISTS "Service role full access to question_difficulties" ON question_difficulties;
DROP POLICY IF EXISTS "Allow all operations on question_explanations" ON question_explanations;
DROP POLICY IF EXISTS "Public read access to question_difficulties" ON question_difficulties;

-- RLS Policies for test_sessions
CREATE POLICY "Service role full access to test_sessions"
ON test_sessions
FOR ALL
TO service_role
USING (true)
WITH CHECK (true);

-- RLS Policies for question_difficulties
CREATE POLICY "Service role full access to question_difficulties"
ON question_difficulties
FOR ALL
TO service_role
USING (true)
WITH CHECK (true);

CREATE POLICY "Public read access to question_difficulties"
ON question_difficulties
FOR SELECT
TO anon, authenticated
USING (true);

-- View: Summary of questions by difficulty level
CREATE OR REPLACE VIEW vw_questions_by_difficulty AS
SELECT 
    difficulty,
    COUNT(*) AS questioncount,
    ROUND(AVG(successrate), 2) AS averagesuccessrate,
    SUM(totalattempts) AS totalattempts,
    SUM(correctattempts) AS correctattempts
FROM question_difficulties
GROUP BY difficulty
ORDER BY 
    CASE difficulty
        WHEN 'easy' THEN 1
        WHEN 'medium' THEN 2
        WHEN 'hard' THEN 3
        ELSE 4
    END;

-- Table: question_explanations
-- Stores explanations for each question to help users learn from their answers
CREATE TABLE IF NOT EXISTS question_explanations (
    questionfile TEXT PRIMARY KEY,
    explanation TEXT,
    createdat TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updatedat TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for faster queries
CREATE INDEX IF NOT EXISTS idx_question_explanations_updated ON question_explanations(updatedat DESC);

-- Trigger to automatically update the 'updatedat' field in question_explanations
CREATE OR REPLACE FUNCTION update_question_explanations_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updatedat = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trigger_update_question_explanations_timestamp ON question_explanations;
CREATE TRIGGER trigger_update_question_explanations_timestamp
    BEFORE UPDATE ON question_explanations
    FOR EACH ROW
    EXECUTE FUNCTION update_question_explanations_timestamp();

-- RLS Policy for question_explanations
CREATE POLICY "Allow all operations on question_explanations"
ON question_explanations
FOR ALL
USING (true)
WITH CHECK (true);

