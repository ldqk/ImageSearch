namespace 以图搜图.Services;

public class IndexProgressEventArgs : EventArgs
{
    public string Filename { get; set; }

    public string Message { get; set; } = string.Empty;
    public double Speed { get; set; }
    public double ThroughputMB { get; set; }
    public int ProcessedFiles { get; set; }
    public int TotalFiles { get; set; }
    public double ProgressPercentage => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
}

public class IndexCompletedEventArgs : EventArgs
{
    public double ElapsedSeconds { get; set; }
    public int FilesProcessed { get; set; }
    public List<string> Errors { get; set; } = new();
}