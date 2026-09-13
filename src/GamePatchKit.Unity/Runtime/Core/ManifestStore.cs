#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Unity
{
    // CLI ManifestStore의 읽기·검증·원자 교체 부분이다. 세대 매니페스트는 받은 바이트 그대로 보관하므로
    // 직렬화 경로는 두지 않는다.
    internal static class ManifestStore
    {
        private const string ManifestFileName = "manifest.json";
        private const string ManifestObjectDirectoryName = "manifests";
        private const string LocalManifestErrorPrefix = "로컬 매니페스트가 올바르지 않습니다.";
        private const int ChecksumLength = 64;
        private static readonly Encoding _utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new LowercaseEnumConverter() }
        };

        public static string GetManifestObjectPath(long releaseVersion)
        {
            return $"{ManifestObjectDirectoryName}/{releaseVersion.ToString(CultureInfo.InvariantCulture)}.json";
        }

        public static PatchManifest? ReadLocal(string rootPath)
        {
            string manifestPath = Path.Combine(rootPath, ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                return null;
            }

            return ParseManifest(File.ReadAllText(manifestPath, _utf8WithoutBom), LocalManifestErrorPrefix);
        }

        public static PatchManifest ReadFromBytes(byte[] bytes, string errorPrefix)
        {
            return ParseManifest(_utf8WithoutBom.GetString(bytes), errorPrefix);
        }

        // 세대 전환으로 data 트리를 바꾸기 시작하면 이전 세대 표식을 먼저 지운다. 중간에 멈춘 트리를 다음 동기화가 이전
        // 세대로 오인하지 않고 전체를 다시 푼다.
        public static void DeleteLocal(string rootPath)
        {
            string manifestPath = Path.Combine(rootPath, ManifestFileName);

            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
        }

        // 받은 세대 매니페스트를 재직렬화 없이 그대로 교체한다. 로컬 manifest.json은 원격 manifests/<releaseVersion>.json과
        // 바이트까지 같다.
        public static void WriteBytesAtomically(string rootPath, byte[] manifestBytes)
        {
            Directory.CreateDirectory(rootPath);
            string manifestPath = Path.Combine(rootPath, ManifestFileName);
            string temporaryPath = Path.Combine(rootPath, $".{ManifestFileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllBytes(temporaryPath, manifestBytes);
                FileMover.MoveReplacing(temporaryPath, manifestPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        internal static bool TryParseEntryVersion(string? value, int groupVersion, out int revision)
        {
            revision = 0;

            if (value is null)
            {
                return false;
            }

            int separatorIndex = value.IndexOf('.');

            if (separatorIndex <= 0 || separatorIndex != value.LastIndexOf('.'))
            {
                return false;
            }

            string groupPart = value.Substring(0, separatorIndex);
            string revisionPart = value.Substring(separatorIndex + 1);

            if (!int.TryParse(groupPart, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedGroupVersion)
                || !int.TryParse(revisionPart, NumberStyles.None, CultureInfo.InvariantCulture, out revision)
                || parsedGroupVersion != groupVersion)
            {
                return false;
            }

            return value == $"{groupVersion}.{revision}";
        }

        private static PatchManifest ParseManifest(string json, string errorPrefix)
        {
            try
            {
                var loadSettings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                };
                JObject root = JObject.Parse(json, loadSettings);
                JsonSerializer serializer = JsonSerializer.Create(_serializerSettings);
                PatchManifest? manifest = root.ToObject<PatchManifest>(serializer);

                if (manifest is null)
                {
                    throw Invalid(errorPrefix, "$", "루트 객체가 필요합니다.");
                }

                Validate(manifest, errorPrefix, root);
                return manifest;
            }
            catch (PatchClientException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw Invalid(errorPrefix, "$", exception.Message);
            }
            catch (OverflowException exception)
            {
                // long 범위를 넘는 정수 리터럴은 Newtonsoft가 BigInteger로 읽고, 이를 좁은 정수 필드로 변환할 때
                // JsonException이 아니라 날것의 OverflowException을 던진다. 다른 잘못된 값과 똑같이 다룬다.
                throw Invalid(errorPrefix, "$", exception.Message);
            }
        }

        private static void Validate(PatchManifest manifest, string errorPrefix, JObject rawRoot)
        {
            if (manifest.SchemaVersion != PatchManifest.CURRENT_SCHEMA_VERSION)
            {
                throw Invalid(errorPrefix, "schemaVersion", $"지원 값은 {PatchManifest.CURRENT_SCHEMA_VERSION}입니다.");
            }

            if (manifest.ReleaseVersion < 0)
            {
                throw Invalid(errorPrefix, "releaseVersion", "0 이상이어야 합니다.");
            }

            if (!RelativePathValidator.IsNormalized(manifest.SourcePath, allowRepositoryRoot: true))
            {
                throw Invalid(errorPrefix, "sourcePath", "정규화된 저장소 기준 상대 경로여야 합니다.");
            }

            if (string.IsNullOrWhiteSpace(manifest.SourceCommit))
            {
                throw Invalid(errorPrefix, "sourceCommit", "빈 문자열일 수 없습니다.");
            }

            if (manifest.Groups is null)
            {
                throw Invalid(errorPrefix, "groups", "목록이 필요합니다.");
            }

            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            JArray? rawGroups = rawRoot["groups"] as JArray;

            for (int groupIndex = 0; groupIndex < manifest.Groups.Count; groupIndex++)
            {
                ManifestGroup? group = manifest.Groups[groupIndex];

                if (group is null)
                {
                    throw Invalid(errorPrefix, $"groups[{groupIndex}]", "그룹 객체가 필요합니다.");
                }

                JObject? rawGroup = rawGroups?[groupIndex] as JObject;
                ValidateGroup(group, groupIndex, rawGroup, groupIds, errorPrefix);
            }
        }

        private static void ValidateGroup(
            ManifestGroup group,
            int groupIndex,
            JObject? rawGroup,
            HashSet<string> groupIds,
            string errorPrefix)
        {
            string groupPath = $"groups[{groupIndex}]";

            if (!RelativePathValidator.IsNormalized(group.Id, allowRepositoryRoot: false))
            {
                throw Invalid(errorPrefix, $"{groupPath}.id", "정규화된 상대 경로여야 합니다.");
            }

            if (!groupIds.Add(group.Id))
            {
                throw Invalid(errorPrefix, $"{groupPath}.id", "중복되었습니다.");
            }

            if (group.Version < 0)
            {
                throw Invalid(errorPrefix, $"{groupPath}.version", "0 이상이어야 합니다.");
            }

            if (!Enum.IsDefined(typeof(PackingKind), group.Packing))
            {
                throw Invalid(errorPrefix, $"{groupPath}.packing", "지원하지 않는 값입니다.");
            }

            if (!Enum.IsDefined(typeof(CompressionKind), group.Compression))
            {
                throw Invalid(errorPrefix, $"{groupPath}.compression", "지원하지 않는 값입니다.");
            }

            bool hasArchiveProperty = rawGroup?.Property("archive") is not null || group.Archive is not null;

            if (group.Packing == PackingKind.File && hasArchiveProperty)
            {
                throw Invalid(errorPrefix, $"{groupPath}.archive", "packing이 file이면 없어야 합니다.");
            }

            if (group.Archive is not null)
            {
                ValidateArchive(group, groupIndex, errorPrefix);
            }

            if (group.Entries is null || group.Entries.Count == 0)
            {
                throw Invalid(errorPrefix, $"{groupPath}.entries", "하나 이상의 엔트리가 필요합니다.");
            }

            var entryPaths = new HashSet<string>(StringComparer.Ordinal);
            var archiveEntries = new List<(ManifestEntry Entry, int Index)>();
            JArray? rawEntries = rawGroup?["entries"] as JArray;

            for (int entryIndex = 0; entryIndex < group.Entries.Count; entryIndex++)
            {
                ManifestEntry? entry = group.Entries[entryIndex];

                if (entry is null)
                {
                    throw Invalid(errorPrefix, $"{groupPath}.entries[{entryIndex}]", "엔트리 객체가 필요합니다.");
                }

                JObject? rawEntry = rawEntries?[entryIndex] as JObject;
                ValidateEntry(group, entry, groupIndex, entryIndex, rawEntry, entryPaths, errorPrefix);

                if (entry.Source == EntrySource.Archive)
                {
                    archiveEntries.Add((entry, entryIndex));
                }
            }

            if (archiveEntries.Count > 0 && group.Archive is null)
            {
                throw Invalid(errorPrefix, $"{groupPath}.archive", "archive source 엔트리가 있으면 필요합니다.");
            }

            ValidateArchiveLayout(group, groupIndex, archiveEntries, errorPrefix);
        }

        private static void ValidateArchive(ManifestGroup group, int groupIndex, string errorPrefix)
        {
            ManifestArchive archive = group.Archive!;
            string archivePath = $"groups[{groupIndex}].archive";
            string expectedName = $"archives/{group.Id}/{group.Version}.gpka";

            if (archive.Name != expectedName)
            {
                throw Invalid(errorPrefix, $"{archivePath}.name", $"{expectedName}이어야 합니다.");
            }

            if (archive.PayloadSize < 0)
            {
                throw Invalid(errorPrefix, $"{archivePath}.payloadSize", "0 이상이어야 합니다.");
            }

            if (archive.StoredSize < 0)
            {
                throw Invalid(errorPrefix, $"{archivePath}.storedSize", "0 이상이어야 합니다.");
            }

            if (!IsChecksum(archive.Checksum))
            {
                throw Invalid(errorPrefix, $"{archivePath}.checksum", "64자리 소문자 SHA-256이어야 합니다.");
            }
        }

        private static void ValidateEntry(
            ManifestGroup group,
            ManifestEntry entry,
            int groupIndex,
            int entryIndex,
            JObject? rawEntry,
            HashSet<string> entryPaths,
            string errorPrefix)
        {
            string entryPath = $"groups[{groupIndex}].entries[{entryIndex}]";

            if (!RelativePathValidator.IsNormalized(entry.Path, allowRepositoryRoot: false))
            {
                throw Invalid(errorPrefix, $"{entryPath}.path", "정규화된 상대 경로여야 합니다.");
            }

            if (!entryPaths.Add(entry.Path))
            {
                throw Invalid(errorPrefix, $"{entryPath}.path", "중복되었습니다.");
            }

            if (!TryParseEntryVersion(entry.Version, group.Version, out _))
            {
                throw Invalid(errorPrefix, $"{entryPath}.version", $"{group.Version}.<0 이상의 리비전> 형식이어야 합니다.");
            }

            if (entry.Size < 0)
            {
                throw Invalid(errorPrefix, $"{entryPath}.size", "0 이상이어야 합니다.");
            }

            if (!Enum.IsDefined(typeof(EntrySource), entry.Source))
            {
                throw Invalid(errorPrefix, $"{entryPath}.source", "지원하지 않는 값입니다.");
            }

            if (group.Packing == PackingKind.File && entry.Source != EntrySource.File)
            {
                throw Invalid(errorPrefix, $"{entryPath}.source", "packing이 file이면 file이어야 합니다.");
            }

            if (entry.Source == EntrySource.Archive)
            {
                ValidateArchiveEntry(entry, entryPath, rawEntry, errorPrefix);
                return;
            }

            ValidateFileEntry(group, entry, entryPath, rawEntry, errorPrefix);
        }

        private static void ValidateArchiveEntry(ManifestEntry entry, string entryPath, JObject? rawEntry, string errorPrefix)
        {
            if (entry.Offset is null || entry.Offset < 0)
            {
                throw Invalid(errorPrefix, $"{entryPath}.offset", "0 이상의 값이 필요합니다.");
            }

            if (entry.Length is null || entry.Length < 0)
            {
                throw Invalid(errorPrefix, $"{entryPath}.length", "0 이상의 값이 필요합니다.");
            }

            if (entry.Length != entry.Size)
            {
                throw Invalid(errorPrefix, $"{entryPath}.length", "size와 같아야 합니다.");
            }

            EnsurePropertyAbsent(rawEntry, entry.Name is not null, "name", entryPath, errorPrefix);
            EnsurePropertyAbsent(rawEntry, entry.StoredSize is not null, "storedSize", entryPath, errorPrefix);
            EnsurePropertyAbsent(rawEntry, entry.Checksum is not null, "checksum", entryPath, errorPrefix);
        }

        private static void ValidateFileEntry(
            ManifestGroup group,
            ManifestEntry entry,
            string entryPath,
            JObject? rawEntry,
            string errorPrefix)
        {
            EnsurePropertyAbsent(rawEntry, entry.Offset is not null, "offset", entryPath, errorPrefix);
            EnsurePropertyAbsent(rawEntry, entry.Length is not null, "length", entryPath, errorPrefix);
            string expectedName = $"files/{group.Id}/{group.Version}/{entry.Path}.v{entry.Version}";

            if (entry.Name != expectedName)
            {
                throw Invalid(errorPrefix, $"{entryPath}.name", $"{expectedName}이어야 합니다.");
            }

            if (entry.StoredSize is null || entry.StoredSize < 0)
            {
                throw Invalid(errorPrefix, $"{entryPath}.storedSize", "0 이상의 값이 필요합니다.");
            }

            if (!IsChecksum(entry.Checksum))
            {
                throw Invalid(errorPrefix, $"{entryPath}.checksum", "64자리 소문자 SHA-256이어야 합니다.");
            }
        }

        private static void ValidateArchiveLayout(
            ManifestGroup group,
            int groupIndex,
            IReadOnlyList<(ManifestEntry Entry, int Index)> archiveEntries,
            string errorPrefix)
        {
            if (group.Archive is null)
            {
                return;
            }

            long previousEnd = 0;

            foreach ((ManifestEntry entry, int entryIndex) in archiveEntries.OrderBy(item => item.Entry.Path, StringComparer.Ordinal))
            {
                long offset = entry.Offset!.Value;
                long length = entry.Length!.Value;
                string entryPath = $"groups[{groupIndex}].entries[{entryIndex}]";

                if (offset > long.MaxValue - length)
                {
                    throw Invalid(errorPrefix, $"{entryPath}.offset", "offset + length가 long 범위를 넘습니다.");
                }

                long end = offset + length;

                if (end > group.Archive.PayloadSize)
                {
                    throw Invalid(errorPrefix, $"{entryPath}.offset", "offset + length가 archive.payloadSize를 넘습니다.");
                }

                if (offset < previousEnd)
                {
                    throw Invalid(errorPrefix, $"{entryPath}.offset", "path 순서의 이전 archive 구간과 겹칩니다.");
                }

                previousEnd = end;
            }
        }

        private static void EnsurePropertyAbsent(
            JObject? rawObject,
            bool modelHasValue,
            string property,
            string objectPath,
            string errorPrefix)
        {
            if (modelHasValue || rawObject?.Property(property) is not null)
            {
                throw Invalid(errorPrefix, $"{objectPath}.{property}", "이 source에서는 없어야 합니다.");
            }
        }

        private static bool IsChecksum(string? value)
        {
            if (value is null || value.Length != ChecksumLength)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        private static PatchClientException Invalid(string prefix, string path, string reason)
        {
            return new PatchClientException($"{prefix} {path}: {reason}");
        }
    }
}
