-- Winternet Quiz - Additional Tables for Test System and Question Difficulty
-- Normalized to lowercase column names

-- Table: test_sessions
-- Stores information about each test session
CREATE TABLE IF NOT EXISTS test_sessions (
    "Token" TEXT PRIMARY KEY,
    "Username" TEXT NOT NULL,
    "StartedUtc" TIMESTAMP WITH TIME ZONE NOT NULL,
    "CompletedUtc" TIMESTAMP WITH TIME ZONE,
    "Status" TEXT NOT NULL CHECK ("Status" IN ('active', 'completed', 'expired')),
    "QuestionsJson" TEXT NOT NULL,
    "AnswersJson" TEXT NOT NULL DEFAULT '[]',
    "CurrentIndex" INTEGER NOT NULL DEFAULT 0,
    "Score" INTEGER NOT NULL DEFAULT 0,
    "MaxScore" INTEGER NOT NULL DEFAULT 0,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Indexes for faster queries by username and status
CREATE INDEX IF NOT EXISTS idx_test_sessions_username ON test_sessions("Username");
CREATE INDEX IF NOT EXISTS idx_test_sessions_status ON test_sessions("Status");
CREATE INDEX IF NOT EXISTS idx_test_sessions_started ON test_sessions("StartedUtc" DESC);

-- Table: question_difficulties
-- Tracks difficulty level and statistics for each question
CREATE TABLE IF NOT EXISTS question_difficulties (
    "QuestionFile" TEXT PRIMARY KEY,
    "Difficulty" TEXT NOT NULL CHECK ("Difficulty" IN ('easy', 'medium', 'hard')),
    "SuccessRate" DECIMAL(5,2) NOT NULL DEFAULT 0,
    "TotalAttempts" INTEGER NOT NULL DEFAULT 0,
    "CorrectAttempts" INTEGER NOT NULL DEFAULT 0,
    "ManualOverride" BOOLEAN NOT NULL DEFAULT FALSE,
    "LastUpdated" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Indexes for faster queries by difficulty
CREATE INDEX IF NOT EXISTS idx_question_difficulties_difficulty ON question_difficulties("Difficulty");
CREATE INDEX IF NOT EXISTS idx_question_difficulties_success_rate ON question_difficulties("SuccessRate");
CREATE INDEX IF NOT EXISTS idx_question_difficulties_last_updated ON question_difficulties("LastUpdated" DESC);

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
        "Difficulty" = CASE
            WHEN "TotalAttempts" = 0 THEN 'unrated'
            WHEN "SuccessRate" >= 70 THEN 'easy'
            WHEN "SuccessRate" >= 40 THEN 'medium'
            ELSE 'hard'
        END,
        "LastUpdated" = CURRENT_TIMESTAMP
    WHERE "ManualOverride" = FALSE;
    
    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$;

-- Trigger to automatically update the 'UpdatedAt' field in test_sessions
CREATE OR REPLACE FUNCTION update_test_sessions_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW."UpdatedAt" = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trigger_update_test_sessions_timestamp ON test_sessions;
CREATE TRIGGER trigger_update_test_sessions_timestamp
    BEFORE UPDATE ON test_sessions
    FOR EACH ROW
    EXECUTE FUNCTION update_test_sessions_timestamp();

-- Trigger to automatically update the 'LastUpdated' field in question_difficulties
CREATE OR REPLACE FUNCTION update_question_difficulties_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW."LastUpdated" = CURRENT_TIMESTAMP;
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
    "Difficulty",
    COUNT(*) AS questioncount,
    ROUND(AVG("SuccessRate"), 2) AS averagesuccessrate,
    SUM("TotalAttempts") AS totalattempts,
    SUM("CorrectAttempts") AS correctattempts
FROM question_difficulties
GROUP BY "Difficulty"
ORDER BY 
    CASE "Difficulty"
        WHEN 'easy' THEN 1
        WHEN 'medium' THEN 2
        WHEN 'hard' THEN 3
        ELSE 4
    END;

-- Table: question_explanations
-- Stores explanations for each question to help users learn from their answers
CREATE TABLE IF NOT EXISTS question_explanations (
    "QuestionFile" TEXT PRIMARY KEY,
    "Explanation" TEXT,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for faster queries
CREATE INDEX IF NOT EXISTS idx_question_explanations_updated ON question_explanations("UpdatedAt" DESC);

-- Trigger to automatically update the 'UpdatedAt' field in question_explanations
CREATE OR REPLACE FUNCTION update_question_explanations_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW."UpdatedAt" = CURRENT_TIMESTAMP;
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

