using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace nrgill28.AtlasSampleScene
{
    public class PopupTargetManager : MonoBehaviour
    {
        public List<PopupTarget> Targets;
        private readonly List<PopupTarget> _setTargets = new List<PopupTarget>();

        private void Awake()
        {
            StartCoroutine(StartSetAsync(3f, 8f, 5, PopupTarget.TargetRange.All));
        }

        private IEnumerator StartSetAsync(float minDelay, float maxDelay, int numTargets,
            PopupTarget.TargetRange ranges)
        {
            // Wait for the specified duration
            yield return new WaitForSeconds(Random.Range(minDelay, maxDelay));

            // Shuffle the list of targets so we get variation in which ones come up
#if H3VR_IMPORTED
            Targets.Shuffle();
#endif
            _setTargets.Clear();

            // Loop over the targets
            foreach (var target in Targets)
            {
                // If this target is in our wanted range set it
                if ((target.Range & ranges) != 0)
                {
                    target.Set = true;
                    _setTargets.Add(target);
                    numTargets--;
                }

                // If we're done setting targets break
                if (numTargets == 0) break;
            }
        }

        public void TargetHit(PopupTarget target)
        {
            // If this target isn't in our set list ignore. Should never happen but best to be safe.
            if (!_setTargets.Contains(target)) return;
            _setTargets.Remove(target);

            // TODO: Temp debug logic, restart if we hit all the targets
            if (_setTargets.Count == 0)
                StartCoroutine(StartSetAsync(3f, 8f, 5, PopupTarget.TargetRange.All));
        }
    }
}