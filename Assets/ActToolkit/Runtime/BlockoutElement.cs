using System;
using UnityEngine;

namespace ActToolkit
{
    public enum BlockoutElementKind
    {
        Block,
        Floor,
        Wall,
        Ramp,
        Platform,
        Cover,
        SpawnPoint,
        Objective,
        TriggerVolume,
        KillZone,
        NavMarker,
        EnemySpawn,
        DummySpawn,
        CombatZone
    }

    [ExecuteAlways]
    public sealed class BlockoutElement : MonoBehaviour
    {
        public string elementId;
        public BlockoutElementKind kind = BlockoutElementKind.Block;
        public string gameplayTag = "arena";
        public int team;
        public bool serverAuthoritativeCollision = true;
        public Vector3 logicalSize = Vector3.one;
        public Color gizmoColor = new Color(0.3f, 0.75f, 1f, 0.45f);

        private void Reset()
        {
            EnsureId();
            logicalSize = transform.localScale;
        }

        private void OnValidate()
        {
            EnsureId();

            if (logicalSize == Vector3.zero)
            {
                logicalSize = transform.localScale;
            }
        }

        public void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                elementId = Guid.NewGuid().ToString("N");
            }
        }

        private void OnDrawGizmos()
        {
            EnsureId();

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = gizmoColor;

            if (kind == BlockoutElementKind.SpawnPoint
                || kind == BlockoutElementKind.NavMarker
                || kind == BlockoutElementKind.EnemySpawn
                || kind == BlockoutElementKind.DummySpawn)
            {
                Gizmos.DrawSphere(Vector3.zero, 0.35f);
                Gizmos.DrawRay(Vector3.zero, Vector3.forward * 1.2f);
            }
            else
            {
                Gizmos.DrawCube(Vector3.zero, Vector3.one);
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;
        }
    }
}
