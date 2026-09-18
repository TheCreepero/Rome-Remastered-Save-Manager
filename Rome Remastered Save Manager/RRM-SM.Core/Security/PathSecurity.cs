using System;
using System.IO;

namespace RRM_SM.Core.Security
{
    /// <summary>
    /// Security utility for defending against path traversal, directory escape, and invalid file paths.
    /// </summary>
    public static class PathSecurity
    {
        /// <summary>
        /// Validates that the childFileName is strictly a filename (not a path with directory traversals or separators),
        /// does not contain invalid characters, and that the combined full path remains strictly contained within baseDirectory.
        /// </summary>
        public static string EnsureSafeChildPath(string baseDirectory, string childFileName)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("Base directory cannot be null or whitespace.", nameof(baseDirectory));
            }

            if (string.IsNullOrWhiteSpace(childFileName))
            {
                throw new ArgumentException("Filename cannot be null or whitespace.", nameof(childFileName));
            }

            // Reject directory traversal attempts and absolute/rooted paths
            if (childFileName.Contains("..") || 
                childFileName.Contains('/') || 
                childFileName.Contains('\\') || 
                Path.IsPathRooted(childFileName))
            {
                throw new ArgumentException($"Potential path traversal detected in filename: '{childFileName}'", nameof(childFileName));
            }

            // Reject invalid filename characters
            if (childFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"Filename contains illegal characters: '{childFileName}'", nameof(childFileName));
            }

            string fullBase = Path.GetFullPath(baseDirectory);
            string fullTarget = Path.GetFullPath(Path.Combine(fullBase, childFileName));

            string normalizedBase = fullBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) 
                + Path.DirectorySeparatorChar;

            if (!fullTarget.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"Access denied: Path '{fullTarget}' escapes allowed directory '{fullBase}'.");
            }

            return fullTarget;
        }

        /// <summary>
        /// Safe non-throwing check for child path validity.
        /// </summary>
        public static bool TryGetSafeChildPath(string baseDirectory, string childFileName, out string? safeFullPath)
        {
            safeFullPath = null;
            try
            {
                safeFullPath = EnsureSafeChildPath(baseDirectory, childFileName);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

