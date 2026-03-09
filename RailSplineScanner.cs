using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RailSplineScanner", "Codex", "1.0.0")]
    [Description("Scans above-ground rail corridors and exports candidate safety data.")]
    public class RailSplineScanner : RustPlugin
    {
        private const string DataFolder = "RailSplineScanner";
        private const float NodeMergeDistance = 6f;

        private PluginConfig _config;
        private ScanSnapshot _lastScan;

        private class PluginConfig
        {
            [JsonProperty(PropertyName = "MinimumRequiredLengthMeters")]
            public float MinimumRequiredLengthMeters = 250f;

            [JsonProperty(PropertyName = "PreferredLengthMeters")]
            public float PreferredLengthMeters = 300f;

            [JsonProperty(PropertyName = "AllowCandidateWithCleanup")]
            public bool AllowCandidateWithCleanup = true;

            [JsonProperty(PropertyName = "ScanNearbyTrainEntitiesRadius")]
            public float ScanNearbyTrainEntitiesRadius = 30f;

            [JsonProperty(PropertyName = "ScanNearbyTrainSpawnsRadius")]
            public float ScanNearbyTrainSpawnsRadius = 40f;

            [JsonProperty(PropertyName = "DebugLogging")]
            public bool DebugLogging = false;

            [JsonProperty(PropertyName = "ExportPrettyJson")]
            public bool ExportPrettyJson = true;
        }

        private class RailSegment
        {
            public BaseEntity Entity;
            public string Prefab;
            public Vector3 Start;
            public Vector3 End;
            public float Length;
            public bool IsUnderground;
        }

        private class Corridor
        {
            public List<int> SegmentIds = new List<int>();
            public List<int> NodePath = new List<int>();
            public float Length;
            public bool BrokenContinuity;
            public bool Underground;
            public bool HasSwitch;
            public Vector3 StartPos;
            public Vector3 EndPos;
            public Vector3 CenterPos;
        }

        private class CandidateRecord
        {
            public string CandidateId;
            public string Grid;
            public bool Safe;
            public string Status;
            public float LengthMeters;
            public int SwitchCount;
            public int NearbyTrainSpawnCount;
            public int NearbyTrainEntityCount;
            public List<string> ReasonFlags = new List<string>();
            public Vector3Dto StartPos;
            public Vector3Dto EndPos;
            public Vector3Dto CenterPos;
        }

        private class ScanSnapshot
        {
            public uint MapSeed;
            public int MapSize;
            public string ScannedAtUtc;
            public ScanSummary Summary;
            public Dictionary<string, List<CandidateRecord>> Grids = new Dictionary<string, List<CandidateRecord>>();
            public List<CandidateRecord> AllCandidates = new List<CandidateRecord>();
        }

        private class ScanSummary
        {
            public int TotalCandidates;
            public int SafeCount;
            public int UnsafeCount;
            public int CandidateWithCleanupCount;
        }

        private class Vector3Dto
        {
            public float x;
            public float y;
            public float z;

            public static Vector3Dto From(Vector3 v)
            {
                return new Vector3Dto
                {
                    x = (float)Math.Round(v.x, 2),
                    y = (float)Math.Round(v.y, 2),
                    z = (float)Math.Round(v.z, 2)
                };
            }
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                    throw new Exception("Config is null");
            }
            catch
            {
                PrintWarning("Generating new configuration file.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        [ChatCommand("scanspline")]
        private void CmdScanSpline(BasePlayer player, string command, string[] args)
        {
            if (player != null && !player.IsAdmin)
            {
                Reply(player, "Эта команда доступна только администратору.");
                return;
            }

            if (args.Length == 0)
            {
                RunFullScan();
                Reply(player, BuildSummaryText(_lastScan));
                return;
            }

            var sub = args[0].ToLowerInvariant();
            switch (sub)
            {
                case "list":
                    ReplyList(player);
                    break;
                case "help":
                    ReplyHelp(player);
                    break;
                case "show":
                    if (args.Length < 2)
                    {
                        Reply(player, "Использование: /scanspline show <grid>");
                        return;
                    }
                    ReplyShowGrid(player, args[1].ToUpperInvariant());
                    break;
                case "showid":
                    if (args.Length < 2)
                    {
                        Reply(player, "Использование: /scanspline showid <candidateId>");
                        return;
                    }
                    ReplyShowId(player, args[1]);
                    break;
                case "export":
                    if (_lastScan == null)
                    {
                        Reply(player, "В памяти нет результатов. Сначала выполните /scanspline.");
                        return;
                    }
                    ExportSnapshot(_lastScan);
                    Reply(player, "Текущий кэш скана повторно экспортирован в oxide/data.");
                    break;
                case "clear":
                    _lastScan = null;
                    Reply(player, "Кэш скана в памяти очищен.");
                    break;
                default:
                    ReplyHelp(player);
                    break;
            }
        }

        private void ReplyHelp(BasePlayer player)
        {
            Reply(player, "RailSplineScanner: список команд");
            Reply(player, "/scanspline - полный скан сети рельс и экспорт");
            Reply(player, "/scanspline list - компактный список всех кандидатов");
            Reply(player, "/scanspline show <grid> - список кандидатов по указанной сетке");
            Reply(player, "/scanspline showid <candidateId> - полная информация по кандидату");
            Reply(player, "/scanspline export - повторный экспорт последнего скана");
            Reply(player, "/scanspline clear - очистка кэша скана в памяти");
            Reply(player, "/scanspline help - показать эту справку");
            Reply(player, "Консольные команды: отсутствуют");
            Reply(player, "Права (permissions): отсутствуют, используется проверка admin");
        }

        private void RunFullScan()
        {
            var railSegments = CollectRailSegments();
            var corridors = BuildCorridors(railSegments);
            var snapshot = EvaluateCorridors(corridors);
            ExportSnapshot(snapshot);
            _lastScan = snapshot;
        }

        private List<RailSegment> CollectRailSegments()
        {
            var result = new List<RailSegment>();

            foreach (var networkable in BaseNetworkable.serverEntities)
            {
                var entity = networkable as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                    continue;

                var prefab = entity.PrefabName?.ToLowerInvariant() ?? string.Empty;
                if (!LooksLikeRail(prefab))
                    continue;

                var center = entity.transform.position;
                var size = entity.bounds.size;
                var halfLen = Mathf.Max(size.x, size.z) * 0.5f;
                if (halfLen < 1f)
                    halfLen = 8f;

                var direction = entity.transform.forward;
                var start = center - direction * halfLen;
                var end = center + direction * halfLen;

                var segment = new RailSegment
                {
                    Entity = entity,
                    Prefab = prefab,
                    Start = start,
                    End = end,
                    Length = Mathf.Max(Vector3.Distance(start, end), 1f),
                    IsUnderground = IsUndergroundRail(entity, prefab)
                };

                if (!segment.IsUnderground)
                    result.Add(segment);
            }

            DebugLog($"Collected rail segments: {result.Count}");
            return result;
        }

        private List<Corridor> BuildCorridors(List<RailSegment> segments)
        {
            var nodes = new List<Vector3>();
            var segmentNodeA = new Dictionary<int, int>();
            var segmentNodeB = new Dictionary<int, int>();
            var adjacency = new Dictionary<int, List<int>>();

            for (var i = 0; i < segments.Count; i++)
            {
                var a = GetOrCreateNode(segments[i].Start, nodes);
                var b = GetOrCreateNode(segments[i].End, nodes);
                segmentNodeA[i] = a;
                segmentNodeB[i] = b;

                if (!adjacency.ContainsKey(a)) adjacency[a] = new List<int>();
                if (!adjacency.ContainsKey(b)) adjacency[b] = new List<int>();
                adjacency[a].Add(i);
                adjacency[b].Add(i);
            }

            var consumedEdges = new HashSet<int>();
            var corridors = new List<Corridor>();

            foreach (var node in adjacency.Keys.ToList())
            {
                if (adjacency[node].Count == 2)
                    continue;

                foreach (var edge in adjacency[node])
                {
                    if (consumedEdges.Contains(edge))
                        continue;
                    corridors.Add(TraceCorridor(node, edge, segments, segmentNodeA, segmentNodeB, adjacency, consumedEdges, nodes));
                }
            }

            for (var i = 0; i < segments.Count; i++)
            {
                if (consumedEdges.Contains(i))
                    continue;

                var startNode = segmentNodeA[i];
                corridors.Add(TraceCorridor(startNode, i, segments, segmentNodeA, segmentNodeB, adjacency, consumedEdges, nodes));
            }

            DebugLog($"Built corridors: {corridors.Count}");
            return corridors;
        }

        private Corridor TraceCorridor(
            int startNode,
            int startEdge,
            List<RailSegment> segments,
            Dictionary<int, int> segmentNodeA,
            Dictionary<int, int> segmentNodeB,
            Dictionary<int, List<int>> adjacency,
            HashSet<int> consumedEdges,
            List<Vector3> nodes)
        {
            var corridor = new Corridor();
            var currentNode = startNode;
            var edge = startEdge;
            var guard = 0;

            corridor.NodePath.Add(currentNode);

            while (guard++ < 10000)
            {
                if (consumedEdges.Contains(edge))
                    break;

                consumedEdges.Add(edge);
                corridor.SegmentIds.Add(edge);

                var a = segmentNodeA[edge];
                var b = segmentNodeB[edge];
                var nextNode = a == currentNode ? b : a;
                corridor.NodePath.Add(nextNode);

                var nodeDegree = adjacency[nextNode].Count;
                if (nodeDegree > 2)
                    corridor.HasSwitch = true;

                if (nodeDegree != 2)
                    break;

                var nextEdge = -1;
                foreach (var candidateEdge in adjacency[nextNode])
                {
                    if (candidateEdge == edge || consumedEdges.Contains(candidateEdge))
                        continue;
                    nextEdge = candidateEdge;
                    break;
                }

                if (nextEdge < 0)
                    break;

                currentNode = nextNode;
                edge = nextEdge;
            }

            corridor.Length = corridor.SegmentIds.Sum(id => segments[id].Length);
            corridor.Underground = corridor.SegmentIds.Any(id => segments[id].IsUnderground);
            corridor.StartPos = nodes[corridor.NodePath.First()];
            corridor.EndPos = nodes[corridor.NodePath.Last()];
            corridor.CenterPos = (corridor.StartPos + corridor.EndPos) * 0.5f;
            corridor.BrokenContinuity = corridor.SegmentIds.Count == 0 || corridor.Length <= 0f;

            return corridor;
        }

        private ScanSnapshot EvaluateCorridors(List<Corridor> corridors)
        {
            var snapshot = new ScanSnapshot
            {
                MapSeed = Convert.ToUInt32(World.Seed),
                MapSize = Convert.ToInt32(World.Size),
                ScannedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Summary = new ScanSummary()
            };

            var countersByGrid = new Dictionary<string, int>();

            foreach (var corridor in corridors)
            {
                var candidate = BuildCandidate(corridor);
                if (!snapshot.Grids.ContainsKey(candidate.Grid))
                    snapshot.Grids[candidate.Grid] = new List<CandidateRecord>();

                if (!countersByGrid.ContainsKey(candidate.Grid))
                    countersByGrid[candidate.Grid] = 0;
                countersByGrid[candidate.Grid]++;
                candidate.CandidateId = $"{candidate.Grid}_{countersByGrid[candidate.Grid]:00}";

                snapshot.Grids[candidate.Grid].Add(candidate);
                snapshot.AllCandidates.Add(candidate);

                snapshot.Summary.TotalCandidates++;
                if (candidate.Status == "Safe") snapshot.Summary.SafeCount++;
                else if (candidate.Status == "CandidateWithCleanup") snapshot.Summary.CandidateWithCleanupCount++;
                else snapshot.Summary.UnsafeCount++;
            }

            return snapshot;
        }

        private CandidateRecord BuildCandidate(Corridor corridor)
        {
            var center = corridor.CenterPos;
            var trainEntityCount = CountNearby(center, _config.ScanNearbyTrainEntitiesRadius, IsBlockingTrainEntity);
            var trainSpawnCount = CountNearby(center, _config.ScanNearbyTrainSpawnsRadius, IsTrainSpawnEntity);

            var flags = new List<string>();
            if (corridor.Length < _config.MinimumRequiredLengthMeters) flags.Add("Short");
            if (corridor.HasSwitch) flags.Add("HasSwitch");
            if (trainSpawnCount > 0) flags.Add("NearbyTrainSpawn");
            if (trainEntityCount > 0) flags.Add("NearbyTrainEntities");
            if (corridor.Underground) flags.Add("Underground");
            if (corridor.BrokenContinuity) flags.Add("BrokenContinuity");

            var status = "Safe";
            var safe = true;

            if (flags.Count > 0)
            {
                var cleanupOnly = _config.AllowCandidateWithCleanup
                                  && flags.All(f => f == "NearbyTrainEntities")
                                  && corridor.Length >= _config.MinimumRequiredLengthMeters
                                  && !corridor.HasSwitch
                                  && trainSpawnCount == 0
                                  && !corridor.Underground
                                  && !corridor.BrokenContinuity;

                if (cleanupOnly)
                {
                    status = "CandidateWithCleanup";
                    safe = false;
                }
                else
                {
                    status = "Unsafe";
                    safe = false;
                }
            }

            return new CandidateRecord
            {
                Grid = ToGrid(corridor.CenterPos),
                Safe = safe,
                Status = status,
                LengthMeters = (float)Math.Round(corridor.Length, 2),
                SwitchCount = corridor.HasSwitch ? 1 : 0,
                NearbyTrainSpawnCount = trainSpawnCount,
                NearbyTrainEntityCount = trainEntityCount,
                ReasonFlags = flags,
                StartPos = Vector3Dto.From(corridor.StartPos),
                EndPos = Vector3Dto.From(corridor.EndPos),
                CenterPos = Vector3Dto.From(corridor.CenterPos)
            };
        }

        private void ExportSnapshot(ScanSnapshot snapshot)
        {
            var fileName = $"{DataFolder}/Map_{snapshot.MapSeed}_{snapshot.MapSize}";
            Interface.Oxide.DataFileSystem.WriteObject(fileName, new
            {
                snapshot.MapSeed,
                snapshot.MapSize,
                snapshot.ScannedAtUtc,
                snapshot.Summary,
                snapshot.Grids
            }, _config.ExportPrettyJson);

            var txtPath = Path.Combine(Interface.Oxide.DataDirectory, DataFolder, $"Map_{snapshot.MapSeed}_{snapshot.MapSize}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(txtPath));
            File.WriteAllText(txtPath, BuildTextSummary(snapshot));
        }

        private string BuildTextSummary(ScanSnapshot snapshot)
        {
            var lines = new List<string>
            {
                $"MapSeed: {snapshot.MapSeed}",
                $"MapSize: {snapshot.MapSize}",
                $"ScannedAtUtc: {snapshot.ScannedAtUtc}",
                $"TotalCandidates: {snapshot.Summary.TotalCandidates}",
                $"Safe: {snapshot.Summary.SafeCount}",
                $"Unsafe: {snapshot.Summary.UnsafeCount}",
                $"CandidateWithCleanup: {snapshot.Summary.CandidateWithCleanupCount}",
                string.Empty
            };

            foreach (var kv in snapshot.Grids.OrderBy(k => k.Key))
            {
                lines.Add($"[{kv.Key}]");
                foreach (var c in kv.Value)
                {
                    lines.Add($"- {c.CandidateId} | {c.LengthMeters}m | {c.Status} | {string.Join(",", c.ReasonFlags)}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void ReplyList(BasePlayer player)
        {
            if (_lastScan == null)
            {
                Reply(player, "В памяти нет результатов. Сначала выполните /scanspline.");
                return;
            }

            Reply(player, $"Кандидатов: {_lastScan.Summary.TotalCandidates}");
            foreach (var candidate in _lastScan.AllCandidates.OrderBy(c => c.Grid).ThenBy(c => c.CandidateId))
            {
                var flags = candidate.ReasonFlags.Count == 0 ? "-" : string.Join(",", candidate.ReasonFlags);
                Reply(player, $"{candidate.Grid} | {candidate.CandidateId} | {candidate.LengthMeters}m | {candidate.Status} | {flags}");
            }
        }

        private void ReplyShowGrid(BasePlayer player, string grid)
        {
            if (_lastScan == null)
            {
                Reply(player, "В памяти нет результатов. Сначала выполните /scanspline.");
                return;
            }

            if (!_lastScan.Grids.TryGetValue(grid, out var list) || list.Count == 0)
            {
                Reply(player, $"В сетке {grid} кандидаты не найдены.");
                return;
            }

            Reply(player, $"Сетка {grid}: {list.Count} кандидат(ов)");
            foreach (var candidate in list.OrderBy(c => c.CandidateId))
            {
                var flags = candidate.ReasonFlags.Count == 0 ? "-" : string.Join(",", candidate.ReasonFlags);
                Reply(player, $"{candidate.CandidateId} | {candidate.LengthMeters}m | {candidate.Status} | {flags}");
            }
        }

        private void ReplyShowId(BasePlayer player, string candidateId)
        {
            if (_lastScan == null)
            {
                Reply(player, "В памяти нет результатов. Сначала выполните /scanspline.");
                return;
            }

            var candidate = _lastScan.AllCandidates.FirstOrDefault(c => c.CandidateId.Equals(candidateId, StringComparison.OrdinalIgnoreCase));
            if (candidate == null)
            {
                Reply(player, $"Кандидат {candidateId} не найден.");
                return;
            }

            var flags = candidate.ReasonFlags.Count == 0 ? "-" : string.Join(",", candidate.ReasonFlags);
            Reply(player, $"CandidateId: {candidate.CandidateId}");
            Reply(player, $"Grid: {candidate.Grid}");
            Reply(player, $"Status: {candidate.Status}");
            Reply(player, $"Safe: {candidate.Safe}");
            Reply(player, $"LengthMeters: {candidate.LengthMeters}");
            Reply(player, $"SwitchCount: {candidate.SwitchCount}");
            Reply(player, $"NearbyTrainSpawnCount: {candidate.NearbyTrainSpawnCount}");
            Reply(player, $"NearbyTrainEntityCount: {candidate.NearbyTrainEntityCount}");
            Reply(player, $"ReasonFlags: {flags}");
            Reply(player, $"StartPos: {candidate.StartPos.x},{candidate.StartPos.y},{candidate.StartPos.z}");
            Reply(player, $"EndPos: {candidate.EndPos.x},{candidate.EndPos.y},{candidate.EndPos.z}");
            Reply(player, $"CenterPos: {candidate.CenterPos.x},{candidate.CenterPos.y},{candidate.CenterPos.z}");
        }

        private string BuildSummaryText(ScanSnapshot snapshot)
        {
            return $"Скан завершен. total candidates={snapshot.Summary.TotalCandidates}, safe={snapshot.Summary.SafeCount}, unsafe={snapshot.Summary.UnsafeCount}, candidate_with_cleanup={snapshot.Summary.CandidateWithCleanupCount}";
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player == null)
            {
                Puts(message);
                return;
            }

            SendReply(player, message);
        }

        private int CountNearby(Vector3 center, float radius, Func<BaseEntity, bool> filter)
        {
            var count = 0;
            var sqr = radius * radius;
            foreach (var networkable in BaseNetworkable.serverEntities)
            {
                var entity = networkable as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                    continue;
                if (!filter(entity))
                    continue;
                if ((entity.transform.position - center).sqrMagnitude <= sqr)
                    count++;
            }
            return count;
        }

        private int GetOrCreateNode(Vector3 position, List<Vector3> nodes)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (Vector3.Distance(nodes[i], position) <= NodeMergeDistance)
                    return i;
            }

            nodes.Add(position);
            return nodes.Count - 1;
        }

        private bool LooksLikeRail(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
                return false;

            if (prefab.Contains("metro") || prefab.Contains("underground") || prefab.Contains("subway"))
                return false;

            return prefab.Contains("rail") || prefab.Contains("traintrack") || prefab.Contains("train_track") || prefab.Contains("track");
        }

        private bool IsUndergroundRail(BaseEntity entity, string prefab)
        {
            if (prefab.Contains("metro") || prefab.Contains("underground") || prefab.Contains("subway") || prefab.Contains("tunnel"))
                return true;

            var pos = entity.transform.position;
            var terrainHeight = TerrainMeta.HeightMap.GetHeight(pos);
            return pos.y + 3f < terrainHeight;
        }

        private bool IsBlockingTrainEntity(BaseEntity entity)
        {
            var prefab = entity.PrefabName?.ToLowerInvariant() ?? string.Empty;
            if (prefab.Contains("spawn"))
                return false;

            return prefab.Contains("train") || prefab.Contains("wagon") || prefab.Contains("locomotive") || prefab.Contains("workcart");
        }

        private bool IsTrainSpawnEntity(BaseEntity entity)
        {
            var prefab = entity.PrefabName?.ToLowerInvariant() ?? string.Empty;
            return prefab.Contains("train") && prefab.Contains("spawn");
        }

        private string ToGrid(Vector3 position)
        {
            var size = World.Size;
            var cell = 146.3f;
            var xIndex = Mathf.Clamp(Mathf.FloorToInt((position.x + size * 0.5f) / cell), 0, 25);
            var zIndex = Mathf.Max(0, Mathf.FloorToInt((size * 0.5f - position.z) / cell));

            var letter = (char)('A' + xIndex);
            return $"{letter}{zIndex}";
        }

        private void DebugLog(string message)
        {
            if (_config.DebugLogging)
                Puts($"[DEBUG] {message}");
        }
    }
}
