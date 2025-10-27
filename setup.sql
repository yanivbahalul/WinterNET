-- Create the WinterUsers table with correct schema (only if it doesn't exist)
CREATE TABLE IF NOT EXISTS public."WinterUsers" (
  "Username" text PRIMARY KEY,
  "Password" text NOT NULL,
  "CorrectAnswers" integer DEFAULT 0,
  "TotalAnswered" integer DEFAULT 0,
  "IsCheater" boolean DEFAULT false,
  "IsBanned" boolean DEFAULT false,
  "LastSeen" timestamp with time zone DEFAULT now(),
  "CreatedAt" timestamp with time zone DEFAULT now()
);

-- Enable Row Level Security (safe to run multiple times)
DO $$ 
BEGIN
  ALTER TABLE public."WinterUsers" ENABLE ROW LEVEL SECURITY;
EXCEPTION WHEN OTHERS THEN NULL;
END $$;

-- Create policy to allow all operations (for public use)
DO $$ 
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies 
    WHERE tablename = 'WinterUsers' AND policyname = 'Allow all operations on WinterUsers'
  ) THEN
    CREATE POLICY "Allow all operations on WinterUsers" 
    ON public."WinterUsers" 
    FOR ALL 
    USING (true) 
    WITH CHECK (true);
  END IF;
EXCEPTION WHEN OTHERS THEN NULL;
END $$;

-- Insert a test user for testing (only if doesn't exist)
INSERT INTO public."WinterUsers" ("Username", "Password", "CorrectAnswers", "TotalAnswered", "IsCheater", "IsBanned") 
VALUES ('testuser', 'testpass', 0, 0, false, false)
ON CONFLICT ("Username") DO NOTHING;
