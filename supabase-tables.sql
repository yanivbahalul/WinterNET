-- Winternet Quiz - Additional Tables for Test System and Question Difficulty
-- Created to support test sessions and question difficulty tracking

-- Table: test_sessions
-- Stores information about each test session
CREATE TABLE IF NOT EXISTS test_sessions (
    Token TEXT PRIMARY KEY,
    Username TEXT NOT NULL,
    StartedUtc TIMESTAMP WITH TIME ZONE NOT NULL,
    CompletedUtc TIMESTAMP WITH TIME ZONE,
    Status TEXT NOT NULL CHECK (Status IN ('active', 'completed', 'expired')),
    QuestionsJson TEXT NOT NULL,
    AnswersJson TEXT NOT NULL DEFAULT '[]',
    CurrentIndex INTEGER NOT NULL DEFAULT 0,
    Score INTEGER NOT NULL DEFAULT 0,
    MaxScore INTEGER NOT NULL DEFAULT 0,
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for faster queries by username and status
CREATE INDEX IF NOT EXISTS idx_test_sessions_username ON test_sessions(Username);
CREATE INDEX IF NOT EXISTS idx_test_sessions_status ON test_sessions(Status);
CREATE INDEX IF NOT EXISTS idx_test_sessions_started ON test_sessions(StartedUtc DESC);

-- Table: question_difficulties
-- Tracks difficulty level and statistics for each question
CREATE TABLE IF NOT EXISTS question_difficulties (
    QuestionFile TEXT PRIMARY KEY,
    Difficulty TEXT NOT NULL CHECK (Difficulty IN ('easy', 'medium', 'hard')),
    SuccessRate DECIMAL(5,2) NOT NULL DEFAULT 0,
    TotalAttempts INTEGER NOT NULL DEFAULT 0,
    CorrectAttempts INTEGER NOT NULL DEFAULT 0,
    ManualOverride BOOLEAN NOT NULL DEFAULT FALSE,
    LastUpdated TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for faster queries by difficulty
CREATE INDEX IF NOT EXISTS idx_question_difficulties_difficulty ON question_difficulties(Difficulty);
CREATE INDEX IF NOT EXISTS idx_question_difficulties_success_rate ON question_difficulties(SuccessRate);
CREATE INDEX IF NOT EXISTS idx_question_difficulties_last_updated ON question_difficulties(LastUpdated DESC);

-- Function to automatically recalculate all difficulties based on success rate
-- Rules: easy >= 65%, medium 35-65%, hard < 35%
CREATE OR REPLACE FUNCTION recalculate_all_difficulties()
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE question_difficulties
    SET 
        Difficulty = CASE
            WHEN SuccessRate >= 65 THEN 'easy'
            WHEN SuccessRate >= 35 THEN 'medium'
            ELSE 'hard'
        END,
        LastUpdated = CURRENT_TIMESTAMP
    WHERE ManualOverride = FALSE;
    
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
    NEW.UpdatedAt = CURRENT_TIMESTAMP;
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
    NEW.LastUpdated = CURRENT_TIMESTAMP;
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

-- RLS Policies for test_sessions
-- Allow service_role full access
CREATE POLICY IF NOT EXISTS "Service role full access to test_sessions"
ON test_sessions
FOR ALL
TO service_role
USING (true)
WITH CHECK (true);

-- RLS Policies for question_difficulties
-- Allow service_role full access
CREATE POLICY IF NOT EXISTS "Service role full access to question_difficulties"
ON question_difficulties
FOR ALL
TO service_role
USING (true)
WITH CHECK (true);

-- Optional: Allow public read access to question difficulties
CREATE POLICY IF NOT EXISTS "Public read access to question_difficulties"
ON question_difficulties
FOR SELECT
TO anon, authenticated
USING (true);

-- View: Summary of questions by difficulty level
-- This view provides aggregated statistics for each difficulty level
CREATE OR REPLACE VIEW vw_questions_by_difficulty AS
SELECT 
    Difficulty,
    COUNT(*) AS QuestionCount,
    ROUND(AVG(SuccessRate), 2) AS AverageSuccessRate,
    SUM(TotalAttempts) AS TotalAttempts,
    SUM(CorrectAttempts) AS CorrectAttempts
FROM question_difficulties
GROUP BY Difficulty
ORDER BY 
    CASE Difficulty
        WHEN 'easy' THEN 1
        WHEN 'medium' THEN 2
        WHEN 'hard' THEN 3
        ELSE 4
    END;

