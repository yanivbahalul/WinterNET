-- Drop existing table if it exists
DROP TABLE IF EXISTS public."WinterUsers";

-- Create the WinterUsers table with correct schema
CREATE TABLE public."WinterUsers" (
  "Username" text PRIMARY KEY,
  "Password" text NOT NULL,
  "CorrectAnswers" integer DEFAULT 0,
  "TotalAnswered" integer DEFAULT 0,
  "IsCheater" boolean DEFAULT false,
  "IsBanned" boolean DEFAULT false,
  "LastSeen" timestamp with time zone DEFAULT now(),
  "CreatedAt" timestamp with time zone DEFAULT now()
);

-- Enable Row Level Security
ALTER TABLE public."WinterUsers" ENABLE ROW LEVEL SECURITY;

-- Create policy to allow all operations (for public use)
CREATE POLICY "Allow all operations on WinterUsers" 
ON public."WinterUsers" 
FOR ALL 
USING (true) 
WITH CHECK (true);

-- Optional: Insert a test user for testing
-- INSERT INTO public."WinterUsers" ("Username", "Password", "CorrectAnswers", "TotalAnswered", "IsCheater", "IsBanned") 
-- VALUES ('testuser', 'password123', 0, 0, false, false);
