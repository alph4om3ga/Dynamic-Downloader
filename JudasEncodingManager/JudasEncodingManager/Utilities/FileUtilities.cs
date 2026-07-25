namespace JudasEncodingManager.Utilities
{
    /// <summary>
    /// Common file-related utility methods used across the application
    /// </summary>
    public static class FileUtilities
    {
        /// <summary>
        /// Formats a file size in bytes to a human-readable string (B, KB, MB, GB)
        /// </summary>
        /// <param name="bytes">The file size in bytes</param>
        /// <returns>A formatted string like "1.50 GB" or "256.00 MB"</returns>
        public static string FormatFileSize(long bytes)
        {
            if (bytes >= 1073741824)
                return $"{bytes / 1073741824.0:F2} GB";
            if (bytes >= 1048576)
                return $"{bytes / 1048576.0:F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }
}
