using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MadeInArizona
{
    /// <summary>Bounded, real-physics handling checks driven through the normal keyboard input path.</summary>
    public static class VehicleHandlingRegression
    {
        public static IEnumerator Run(Keyboard keyboard, Action<string, bool> check)
        {
            var game = GameManager.Instance;
            var player = game != null ? game.Player : null;
            if (keyboard == null || player == null || !game.IsPlaying)
            {
                check("handling regression prerequisites", false);
                yield break;
            }

            Vector3 originalPosition = player.Body.position;
            Quaternion originalRotation = player.Body.rotation;
            Vector3 originalVelocity = player.Body.linearVelocity;
            Vector3 originalAngularVelocity = player.Body.angularVelocity;

            var arena = new GameObject("Handling regression arena");
            arena.transform.SetParent(game.World.transform);
            Vector3 origin = new Vector3(72, 18, 112);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Handling regression floor";
            floor.transform.SetParent(arena.transform);
            floor.transform.position = origin + Vector3.down;
            floor.transform.localScale = new Vector3(32, 1, 32);

            yield return Place(player, keyboard, origin, Quaternion.identity);
            Vector3 start = player.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return new WaitForSeconds(1.8f);
            Release(keyboard);
            Vector3 turnDelta = player.transform.position - start;
            check("responsive 90 degree launch turn", turnDelta.x > 2f && Vector3.Dot(player.transform.forward, Vector3.right) > .65f);

            var breakable = GameObject.CreatePrimitive(PrimitiveType.Cube);
            breakable.name = "Handling regression small breakable";
            breakable.transform.SetParent(arena.transform);
            breakable.transform.position = origin + new Vector3(0, .05f, 5.5f);
            breakable.transform.localScale = new Vector3(1, 1.1f, 1);
            var breakableDamage = breakable.AddComponent<DestructionSystem>();
            breakableDamage.Configure(1, ExplosionKind.Gasoline, false, 0);
            yield return Place(player, keyboard, origin, Quaternion.identity);
            player.Body.linearVelocity = Vector3.forward * 14f;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(.55f);
            Release(keyboard);
            check("small breakable retains driving momentum", breakableDamage == null && player.transform.position.z > origin.z + 6f && player.Body.linearVelocity.z > 10f);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Handling regression wall";
            wall.transform.SetParent(arena.transform);
            wall.transform.localScale = new Vector3(12, 3, .8f);
            wall.transform.position = origin + new Vector3(0, .5f, 3.4f);
            var wallProbe = wall.AddComponent<HandlingWallProbe>();
            yield return Place(player, keyboard, origin, Quaternion.identity);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(.8f);
            Vector3 impactPosition = player.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S));
            yield return new WaitForSeconds(1.5f);
            Release(keyboard);
            check("head-on wall reverse escape", wallProbe.ContactedPlayer && player.transform.position.z < impactPosition.z - .8f);

            wall.transform.rotation = Quaternion.Euler(0, 30, 0);
            wallProbe.ContactedPlayer = false;
            Physics.SyncTransforms();
            yield return Place(player, keyboard, origin, Quaternion.identity);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(.8f);
            impactPosition = player.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S, Key.A));
            yield return new WaitForSeconds(1.6f);
            Release(keyboard);
            Vector3 retreat = player.transform.position - impactPosition;
            check("angled wall reverse escape", wallProbe.ContactedPlayer && Vector3.Dot(retreat, new Vector3(-1, 0, -1).normalized) > .8f);

            Release(keyboard);
            player.Body.position = originalPosition;
            player.Body.rotation = originalRotation;
            player.Body.linearVelocity = originalVelocity;
            player.Body.angularVelocity = originalAngularVelocity;
            player.transform.SetPositionAndRotation(originalPosition, originalRotation);
            Physics.SyncTransforms();
            UnityEngine.Object.Destroy(arena);
            yield return null;
        }

        static IEnumerator Place(VehicleController player, Keyboard keyboard, Vector3 position, Quaternion rotation)
        {
            Release(keyboard);
            player.Body.linearVelocity = Vector3.zero;
            player.Body.angularVelocity = Vector3.zero;
            player.Body.position = position;
            player.Body.rotation = rotation;
            player.transform.SetPositionAndRotation(position, rotation);
            player.Repair(10000);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.35f);
        }

        static void Release(Keyboard keyboard)
        {
            if (keyboard != null) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }
    }

    public sealed class HandlingWallProbe : MonoBehaviour
    {
        public bool ContactedPlayer { get; set; }

        void OnCollisionEnter(Collision collision) { Record(collision); }
        void OnCollisionStay(Collision collision) { Record(collision); }

        void Record(Collision collision)
        {
            var vehicle = collision.collider.GetComponentInParent<VehicleController>();
            if (vehicle != null && vehicle.IsPlayer) ContactedPlayer = true;
        }
    }
}
