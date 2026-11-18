using System;
using System.IO;

namespace SaruTask
{
    internal static class RenameService
    {
        internal static string Rename(string source, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == "." || newName == ".." ||
                newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.EndsWith(" ", StringComparison.Ordinal) || newName.EndsWith(".", StringComparison.Ordinal))
                throw new ArgumentException("ファイル名に使用できない文字や末尾の空白・ピリオドが含まれています。");
            string original = Path.GetFullPath(source);
            string parent = Path.GetDirectoryName(original);
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException("この場所の名前は変更できません。");
            bool directory = (File.GetAttributes(original) & FileAttributes.Directory) != 0;
            string destination = Path.Combine(parent, newName);
            if (string.Equals(original, destination, StringComparison.Ordinal)) return original;
            bool caseOnly = string.Equals(original, destination, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (File.Exists(destination) || Directory.Exists(destination))) throw new IOException("同じ名前のファイルまたはフォルダが既にあります。");
            if (!caseOnly) { Move(original, destination, directory); return destination; }

            // Directory.Move rejects case-only changes on Windows. Use a unique
            // sibling name and restore the original if the second move fails.
            string temporary = Path.Combine(parent, ".sarutask-rename-" + Guid.NewGuid().ToString("N"));
            Move(original, temporary, directory);
            try { Move(temporary, destination, directory); }
            catch (Exception renameError)
            {
                try { Move(temporary, original, directory); }
                catch (Exception restoreError) { throw new IOException("名前変更と元の名前への復元に失敗しました。次の場所を確認してください。\n" + temporary, new AggregateException(renameError, restoreError)); }
                throw;
            }
            return destination;
        }

        private static void Move(string source, string destination, bool directory)
        {
            if (directory) Directory.Move(source, destination);
            else File.Move(source, destination);
        }
    }
}
