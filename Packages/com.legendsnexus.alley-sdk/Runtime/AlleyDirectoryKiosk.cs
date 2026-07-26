using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Economy;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // the brain behind the booth directory kiosk. the exhibitor list on the
    // left fills the detail panel on the right, and the two buttons act on
    // whatever is selected. everything is local, no sync, so a crowd can all
    // browse different booths on the same board at once
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    public class AlleyDirectoryKiosk : UdonSharpBehaviour
    {
        [Header("Detail panel, wired by the directory builder")]
        public GameObject placeholder;
        public GameObject detail;
        public TextMeshProUGUI nameLabel;
        public TextMeshProUGUI plotLabel;
        public TextMeshProUGUI bodyLabel;
        public Image logoImage;
        public GameObject logoFallback;
        public TextMeshProUGUI logoLetter;
        public GameObject joinButton;

        [Header("Rows, index matched")]
        public Image[] rowFills;
        public Image[] rowMarkers;
        public TextMeshProUGUI[] rowLabels;

        [Header("Booth data, index matched")]
        public string[] boothNames;
        public string[] boothPlots;
        public string[] boothBodies;
        public string[] boothGroupIds;
        public Sprite[] boothLogos;
        public Transform[] boothTargets;

        [Header("Row colours")]
        public Color rowIdle = new Color(0.078f, 0.086f, 0.102f, 1f);
        public Color rowActive = new Color(0.153f, 0.106f, 0.212f, 1f);
        public Color markerIdle = new Color(0.165f, 0.176f, 0.2f, 1f);
        public Color markerActive = new Color(1f, 0f, 0.478f, 1f);
        public Color labelIdle = new Color(0.84f, 0.85f, 0.87f, 1f);
        public Color labelActive = Color.white;

        private int _selected = -1;

        private void Start()
        {
            Clear();
        }

        // ui events cannot start with an underscore, the row buttons call this
        // one through their own entry behaviour
        public void Show(int index)
        {
            if (boothNames == null || index < 0 || index >= boothNames.Length) return;
            _selected = index;

            if (placeholder != null) placeholder.SetActive(false);
            if (detail != null) detail.SetActive(true);

            if (nameLabel != null) nameLabel.text = boothNames[index];
            if (plotLabel != null) plotLabel.text = "PLOT " + boothPlots[index];
            if (bodyLabel != null)
            {
                string body = boothBodies[index];
                bodyLabel.text = string.IsNullOrEmpty(body)
                    ? "This community has not written an introduction yet."
                    : body;
            }

            Sprite logo = boothLogos[index];
            if (logoImage != null)
            {
                logoImage.sprite = logo;
                logoImage.gameObject.SetActive(logo != null);
            }
            if (logoFallback != null) logoFallback.SetActive(logo == null);
            if (logoLetter != null)
            {
                string title = boothNames[index];
                logoLetter.text = string.IsNullOrEmpty(title) ? "?" : title.Substring(0, 1).ToUpper();
            }
            if (joinButton != null) joinButton.SetActive(!string.IsNullOrEmpty(boothGroupIds[index]));

            Paint();
        }

        public void OnTeleport()
        {
            if (_selected < 0 || boothTargets == null || _selected >= boothTargets.Length) return;
            Transform target = boothTargets[_selected];
            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null || target == null) return;
            player.TeleportTo(target.position, target.rotation);
        }

        public void OnJoin()
        {
            if (_selected < 0 || boothGroupIds == null || _selected >= boothGroupIds.Length) return;
            string groupId = boothGroupIds[_selected];
            if (string.IsNullOrEmpty(groupId)) return;
            Store.OpenGroupPage(groupId);
        }

        // nothing picked yet, the panel sits on its prompt
        private void Clear()
        {
            _selected = -1;
            if (placeholder != null) placeholder.SetActive(true);
            if (detail != null) detail.SetActive(false);
            Paint();
        }

        private void Paint()
        {
            if (rowFills == null) return;
            for (int i = 0; i < rowFills.Length; i++)
            {
                bool active = i == _selected;
                if (rowFills[i] != null) rowFills[i].color = active ? rowActive : rowIdle;
                if (rowMarkers != null && i < rowMarkers.Length && rowMarkers[i] != null)
                {
                    rowMarkers[i].color = active ? markerActive : markerIdle;
                }
                if (rowLabels != null && i < rowLabels.Length && rowLabels[i] != null)
                {
                    rowLabels[i].color = active ? labelActive : labelIdle;
                }
            }
        }
    }
}
