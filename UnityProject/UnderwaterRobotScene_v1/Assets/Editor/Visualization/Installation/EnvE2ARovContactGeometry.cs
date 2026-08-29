using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UnderwaterRobotScene.EditorTools
{
    internal readonly struct EnvE2AContactPoint
    {
        internal EnvE2AContactPoint(
            string role,
            string sourcePath,
            Vector3 world)
        {
            Role = role;
            SourcePath = sourcePath;
            World = world;
        }

        internal string Role { get; }
        internal string SourcePath { get; }
        internal Vector3 World { get; }
    }

    internal sealed class EnvE2AContactAuthorityEvidence
    {
        internal EnvE2AContactAuthorityEvidence(
            string rovRootPath,
            string[] auditedSourcePaths,
            EnvE2AContactPoint[] acceptedPoints,
            string[] rejectedCandidates,
            string decisionRule)
        {
            Schema = "ENV-E2A-ROV-Contact-Authority-Evidence-v1";
            RovRootPath = rovRootPath;
            AuditedSourcePaths = auditedSourcePaths;
            AcceptedPoints = acceptedPoints;
            RejectedCandidates = rejectedCandidates;
            DecisionRule = decisionRule;
        }

        internal string Schema { get; }
        internal string RovRootPath { get; }
        internal string[] AuditedSourcePaths { get; }
        internal EnvE2AContactPoint[] AcceptedPoints { get; }
        internal string[] RejectedCandidates { get; }
        internal string DecisionRule { get; }

        internal string CanonicalJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\"Schema\":");
            AppendJson(builder, Schema);
            builder.Append(",\"RovRootPath\":");
            AppendJson(builder, RovRootPath);
            builder.Append(",\"AuditedSourcePaths\":[");
            for (int index = 0;
                index < AuditedSourcePaths.Length;
                index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendJson(builder, AuditedSourcePaths[index]);
            }

            builder.Append("],\"AcceptedPoints\":[");
            for (int index = 0;
                index < AcceptedPoints.Length;
                index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                EnvE2AContactPoint point = AcceptedPoints[index];
                builder.Append("{\"Role\":");
                AppendJson(builder, point.Role);
                builder.Append(",\"SourcePath\":");
                AppendJson(builder, point.SourcePath);
                builder.Append(",\"World\":");
                AppendVector(builder, point.World);
                builder.Append('}');
            }

            builder.Append("],\"RejectedCandidates\":[");
            for (int index = 0;
                index < RejectedCandidates.Length;
                index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendJson(builder, RejectedCandidates[index]);
            }

            builder.Append("],\"DecisionRule\":");
            AppendJson(builder, DecisionRule);
            builder.Append('}');
            return builder.ToString();
        }

        internal string Sha256()
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha.ComputeHash(
                            Encoding.UTF8.GetBytes(CanonicalJson())))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static void AppendVector(
            StringBuilder builder,
            Vector3 value)
        {
            builder.Append("{\"x\":");
            builder.Append(value.x.ToString(
                "R",
                CultureInfo.InvariantCulture));
            builder.Append(",\"y\":");
            builder.Append(value.y.ToString(
                "R",
                CultureInfo.InvariantCulture));
            builder.Append(",\"z\":");
            builder.Append(value.z.ToString(
                "R",
                CultureInfo.InvariantCulture));
            builder.Append('}');
        }

        private static void AppendJson(
            StringBuilder builder,
            string value)
        {
            builder.Append('"');
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            builder.Append('"');
        }
    }

    internal static class EnvE2ARovContactGeometry
    {
        private sealed class Candidate
        {
            internal string SourcePath;
            internal Vector3 RovLocal;
            internal Vector3 World;
        }

        private static readonly string[] SourcePaths =
        {
            "/ROV_Box_Seabed/ROV_FineModel_V1_Imported/" +
            "ROV_Bottom_Exoskeleton_LeftSkid",
            "/ROV_Box_Seabed/ROV_FineModel_V1_Imported/" +
            "ROV_Bottom_Exoskeleton_RightSkid",
            "/ROV_Box_Seabed/ROV_FineModel_V1_Imported/" +
            "ROV_Bottom_Central_Stability_LeftFootMount",
            "/ROV_Box_Seabed/ROV_FineModel_V1_Imported/" +
            "ROV_Bottom_Central_Stability_RightFootMount"
        };

        internal static bool TryResolveContactAuthority(
            Transform rovRoot,
            out EnvE2AContactPoint[] contactPoints,
            out EnvE2AContactAuthorityEvidence evidence)
        {
            contactPoints = null;
            evidence = null;
            if (rovRoot == null ||
                !string.Equals(
                    rovRoot.name,
                    "ROV_Box_Seabed",
                    StringComparison.Ordinal))
            {
                return false;
            }

            var candidates = new List<Candidate>();
            foreach (string sourcePath in SourcePaths)
            {
                Transform source = FindRelative(
                    rovRoot,
                    sourcePath);
                if (source == null ||
                    !TryReadUniqueMesh(
                        source,
                        out Mesh mesh,
                        out Transform meshTransform))
                {
                    return false;
                }

                Vector3[] vertices;
                try
                {
                    vertices = mesh.vertices;
                }
                catch (Exception)
                {
                    return false;
                }

                Vector3[] worldVertices = vertices
                    .Select(meshTransform.TransformPoint)
                    .ToArray();
                if (worldVertices.Length == 0)
                {
                    return false;
                }

                float minimumY =
                    worldVertices.Min(vertex => vertex.y);
                var keys = new HashSet<string>(
                    StringComparer.Ordinal);
                foreach (Vector3 world in worldVertices
                    .Where(vertex =>
                        vertex.y <= minimumY + 0.002f)
                    .OrderBy(VectorRecord, StringComparer.Ordinal))
                {
                    string key = QuantizedRecord(world);
                    if (!keys.Add(key))
                    {
                        continue;
                    }

                    candidates.Add(new Candidate
                    {
                        SourcePath = sourcePath,
                        RovLocal =
                            rovRoot.InverseTransformPoint(world),
                        World = world
                    });
                }
            }

            var accepted = new List<EnvE2AContactPoint>();
            var acceptedKeys = new HashSet<string>(
                StringComparer.Ordinal);
            for (int xSign = -1; xSign <= 1; xSign += 2)
            {
                for (int zSign = -1; zSign <= 1; zSign += 2)
                {
                    Candidate selected = candidates
                        .Where(candidate =>
                            (xSign < 0
                                ? candidate.RovLocal.x < 0f
                                : candidate.RovLocal.x >= 0f) &&
                            (zSign < 0
                                ? candidate.RovLocal.z < 0f
                                : candidate.RovLocal.z >= 0f))
                        .OrderBy(candidate => candidate.RovLocal.y)
                        .ThenByDescending(candidate =>
                            candidate.RovLocal.x *
                            candidate.RovLocal.x +
                            candidate.RovLocal.z *
                            candidate.RovLocal.z)
                        .ThenBy(
                            candidate => candidate.SourcePath,
                            StringComparer.Ordinal)
                        .ThenBy(
                            candidate => VectorRecord(candidate.World),
                            StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (selected == null)
                    {
                        return false;
                    }

                    string role =
                        (xSign < 0 ? "Left" : "Right") +
                        (zSign < 0 ? "Rear" : "Front");
                    accepted.Add(new EnvE2AContactPoint(
                        role,
                        selected.SourcePath,
                        selected.World));
                    acceptedKeys.Add(
                        selected.SourcePath + "|" +
                        QuantizedRecord(selected.World));
                }
            }

            if (accepted.Select(point =>
                    QuantizedRecord(point.World))
                .Distinct(StringComparer.Ordinal)
                .Count() != 4)
            {
                return false;
            }

            contactPoints = accepted
                .OrderBy(point => point.Role, StringComparer.Ordinal)
                .ToArray();
            string[] rejected = candidates
                .Where(candidate => !acceptedKeys.Contains(
                    candidate.SourcePath + "|" +
                    QuantizedRecord(candidate.World)))
                .Select(candidate =>
                    candidate.SourcePath + "|" +
                    VectorRecord(candidate.RovLocal) + "|" +
                    VectorRecord(candidate.World))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            evidence = new EnvE2AContactAuthorityEvidence(
                "/ROV_Box_Seabed",
                (string[])SourcePaths.Clone(),
                contactPoints,
                rejected,
                "Four-source 0.002m bottom band; 0.001m " +
                "deduplication; one deterministic extremal support " +
                "point in each ROV-local X/Z quadrant.");
            return true;
        }

        internal static EnvE2AContactPoint[] ResolveForVerification(
            Transform rovRoot)
        {
            if (!TryResolveContactAuthority(
                rovRoot,
                out EnvE2AContactPoint[] points,
                out EnvE2AContactAuthorityEvidence evidence))
            {
                throw new InvalidOperationException(
                    "ENV_E2A_ROV_CONTACT_AUTHORITY_AMBIGUOUS | " +
                    "USER_REVIEW_REQUIRED");
            }

            if (string.IsNullOrEmpty(evidence.CanonicalJson()) ||
                string.IsNullOrEmpty(evidence.Sha256()))
            {
                throw new InvalidOperationException(
                    "ROV contact evidence is incomplete.");
            }

            return points;
        }

        private static Transform FindRelative(
            Transform rovRoot,
            string absolutePath)
        {
            string prefix = "/" + rovRoot.name + "/";
            if (!absolutePath.StartsWith(
                prefix,
                StringComparison.Ordinal))
            {
                return null;
            }

            string relative = absolutePath.Substring(prefix.Length);
            return rovRoot.Find(relative);
        }

        private static bool TryReadUniqueMesh(
            Transform source,
            out Mesh mesh,
            out Transform meshTransform)
        {
            MeshFilter[] filters =
                source.GetComponentsInChildren<MeshFilter>(true);
            SkinnedMeshRenderer[] skinned =
                source.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true);
            if (filters.Length + skinned.Length != 1)
            {
                mesh = null;
                meshTransform = null;
                return false;
            }

            if (filters.Length == 1)
            {
                mesh = filters[0].sharedMesh;
                meshTransform = filters[0].transform;
            }
            else
            {
                mesh = skinned[0].sharedMesh;
                meshTransform = skinned[0].transform;
            }

            return mesh != null;
        }

        private static string QuantizedRecord(Vector3 value)
        {
            return Mathf.Round(value.x * 1000f) + "|" +
                Mathf.Round(value.y * 1000f) + "|" +
                Mathf.Round(value.z * 1000f);
        }

        private static string VectorRecord(Vector3 value)
        {
            return value.x.ToString("R", CultureInfo.InvariantCulture) +
                "," +
                value.y.ToString("R", CultureInfo.InvariantCulture) +
                "," +
                value.z.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
