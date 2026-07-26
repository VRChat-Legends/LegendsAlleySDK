using UnityEngine;

namespace LegendsNexus.Alley
{
    // holds the loose images until they get baked into the atlas. plain
    // MonoBehaviour so vrchat strips it out of the world build
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Slideshow Source")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleySlideshowSource : MonoBehaviour
    {
        [Tooltip("Your slides, in order. Bake them from the inspector when you are happy with the list.")]
        public Texture2D[] slides = new Texture2D[0];

        [Tooltip("Atlas size the slides get packed into. Bigger looks sharper and costs more.")]
        public int atlasSize = 2048;

        [HideInInspector] public string bakedAt = "";
    }
}
