using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace ValheimMotorBoat
{
    [BepInPlugin("swaz.surtlingboat", "SurtlingBoat", "1.0.0.0")]
    public class MotorBoatPlugin : BaseUnityPlugin
    {
        internal static Harmony Harmony;
        internal new static ManualLogSource Logger;

        private bool _wasKeyDown;

        internal GameObject BoostIcon => _boostIcon != null ? _boostIcon : _boostIcon = CreateBoostIcon();
        private GameObject _boostIcon;

        internal GameObject BoostCrossIcon =>
            _boostCrossIcon != null ? _boostCrossIcon : _boostCrossIcon = CreateBoostCrossIcon();

        private GameObject _boostCrossIcon;

        internal static ConfigEntry<bool> ConfigCheatMode;

        internal static ConfigEntry<float> ConfigSecsPerCore;

        internal static Dictionary<Ship.Speed, ConfigEntry<float>> ConfigSpeed;

        void Awake()
        {
            Logger = base.Logger;

            ConfigSecsPerCore = Config.Bind("General",
                "SecsPerCore",
                5f * 60,
                "Seconds of full speed travel per core");

            var descr = "Amount of speed the Surtling core adds";
            ConfigSpeed = new Dictionary<Ship.Speed, ConfigEntry<float>>
            {
                {
                    Ship.Speed.Back, Config.Bind("General", "SpeedBack", 2.5f, descr)
                },
                {
                    Ship.Speed.Slow, Config.Bind("General", "SpeedSlow", 2.5f, descr)
                },
                {
                    Ship.Speed.Half, Config.Bind("General", "SpeedHalf", 5f, descr)
                },
                {
                    Ship.Speed.Full, Config.Bind("General", "SpeedFull", 10f, descr)
                }
            };

            ConfigCheatMode = Config.Bind("General",
                "CheatMode",
                false,
                "Allow free usage");

            Harmony = new Harmony("swaz.surtlingboat");
            Harmony.PatchAll(typeof(PatchBoat));
        }

        private void OnDestroy()
        {
            Harmony?.UnpatchSelf();

            SafeDestroy(ref _boostIcon);
            SafeDestroy(ref _boostCrossIcon);
        }

        private static void SafeDestroy(ref GameObject gameObject)
        {
            if (gameObject == null) return;
            Destroy(gameObject);
            gameObject = null;
        }

        private static GameObject CreateBoostIcon()
        {
            var itemPrefab = ObjectDB.instance.GetItemPrefab("SurtlingCore");
            var itemDrop = itemPrefab.GetComponent<ItemDrop>();
            var sprite = itemDrop.m_itemData.GetIcon();

            var boostIcon = new GameObject();
            var image = boostIcon.AddComponent<Image>();
            image.sprite = sprite;
            return boostIcon;
        }

        private static GameObject CreateBoostCrossIcon()
        {
            var texture = new Texture2D(1, 1);
            texture.LoadImage(GetResource("ValheimMotorBoat.cross.png"));

            var sprite = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));

            var boostCrossIcon = new GameObject();
            var image = boostCrossIcon.AddComponent<Image>();
            image.sprite = sprite;
            image.color = new Color(255, 255, 255, 0.6f);
            return boostCrossIcon;
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null || !player.TakeInput() || player.GetControlledShip() == null)
            {
                return;
            }

            var ship = player.GetControlledShip();
            var isKeyDown = Input.GetKeyDown(KeyCode.LeftShift);
            var didPress = isKeyDown && !_wasKeyDown;
            _wasKeyDown = isKeyDown;

            if (didPress)
            {
                ship.SetSurtlingEnabled(!ship.GetSurtlingEnabled());
            }

            var boostIconTransform = BoostIcon.GetComponent<RectTransform>();
            boostIconTransform.SetParent(Hud.m_instance.m_shipWindIndicatorRoot);
            boostIconTransform.localPosition = new Vector3(0, 0);
            boostIconTransform.localRotation = new Quaternion();
            boostIconTransform.sizeDelta = new Vector2(25f, 25f);

            BoostIcon.SetActive(ship.GetSurtlingEnabled());


            var crossIconTransform = BoostCrossIcon.GetComponent<RectTransform>();
            crossIconTransform.SetParent(Hud.m_instance.m_shipWindIndicatorRoot);
            crossIconTransform.localPosition = new Vector3(0, 0);
            crossIconTransform.localRotation = new Quaternion();
            crossIconTransform.sizeDelta = new Vector2(20f, 20f);

            BoostCrossIcon.SetActive(ship.GetSurtlingEnabled() && ship.GetSurtlingTicksRemaining() <= 0);
        }

        public static byte[] GetResource(String filename)
        {
            Assembly a = Assembly.GetExecutingAssembly();
            using (Stream stream = a.GetManifestResourceStream(filename))
            {
                if (stream == null) return null;
                byte[] ba = new byte[stream.Length];
                stream.Read(ba, 0, ba.Length);
                return ba;
            }
        }
    }

    [HarmonyPatch(typeof(Ship))]
    public static class PatchBoat
    {
        public static float GetSurtlingTicksRemaining(this Ship ship) => MotorBoatPlugin.ConfigCheatMode.Value
            ? SurtlingTicksPerCore
            : ship.m_nview.GetZDO().GetFloat("surtlingTicksRemaining");

        public static void SetSurtlingTicksRemaining(this Ship ship, float value)
        {
            if (MotorBoatPlugin.ConfigCheatMode.Value) return;
            ship.m_nview.GetZDO().Set("surtlingTicksRemaining", value);
        }

        public static bool GetSurtlingEnabled(this Ship ship) =>
            ship.m_nview.GetZDO().GetBool("surtlingEnabled");

        public static void SetSurtlingEnabled(this Ship ship, bool value) =>
            ship.m_nview.GetZDO().Set("surtlingEnabled", value);

        public static float SurtlingTicksPerCore => MotorBoatPlugin.ConfigSecsPerCore.Value / Time.fixedDeltaTime * 10f;

        [HarmonyPostfix]
        [HarmonyPatch("FixedUpdate")]
        public static void Post_FixedUpdate(Ship __instance)
        {
            Container container = __instance.GetComponentInChildren<Container>();
            if (container == null || container.m_inventory == null || !__instance.GetSurtlingEnabled())
            {
                return;
            }

            var transform = __instance.transform;
            var position = transform.position + transform.forward * __instance.m_stearForceOffset;

            // var hasWind = __instance.GetWindAngleFactor() != 0;
            var boostMulti = 0f;
            var boostDir = __instance.m_speed == Ship.Speed.Back ? -1 : 1;

            MotorBoatPlugin.ConfigSpeed.TryGetValue(__instance.m_speed, out var configEntry);
            if (configEntry != null)
            {
                boostMulti = configEntry.Value;
            }

            var surtlingTicksRemaining = __instance.GetSurtlingTicksRemaining();
            if (surtlingTicksRemaining <= 0)
            {
                if (container.m_inventory.HaveItem("$item_surtlingcore"))
                {
                    container.m_inventory.RemoveItem("$item_surtlingcore", 1);

                    surtlingTicksRemaining += SurtlingTicksPerCore;
                }
            }

            if (surtlingTicksRemaining > 0 && boostMulti > 0)
            {
                surtlingTicksRemaining -= boostMulti;
                __instance.m_body.AddForceAtPosition(
                    boostDir * __instance.transform.forward * (__instance.m_backwardForce * boostMulti) *
                    (1f - Mathf.Abs(__instance.m_rudderValue)) * Time.fixedDeltaTime, position,
                    ForceMode.VelocityChange);
            }

            __instance.SetSurtlingTicksRemaining(surtlingTicksRemaining);
        }
    }
}