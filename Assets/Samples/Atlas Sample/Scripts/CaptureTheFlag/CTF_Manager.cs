using UnityEngine;
#if H3VR_IMPORTED
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FistVR;
using Sodalite.Api;
using Sodalite.Utilities;
using UnityEngine.UI;
#endif

namespace nrgill28.AtlasSampleScene
{
    public class CTF_Manager : MonoBehaviour
    {
#if H3VR_IMPORTED
        [Header("References")]
        public Text[] ScoreTexts;
        public Transform[] AttackPoints;
        public Text StartButtonText;

        [Header("Red Team")]
        public CTF_Flag RedFlag;
        public int RedTeamSize;
        public Transform[] RedSpawns;
        public SosigEnemyID[] RedTeam;

        [Header("Blue Team")]
        public CTF_Flag BlueFlag;
        public int BlueTeamSize;
        public Transform[] BlueSpawns;
        public SosigEnemyID[] BlueTeam;

        // Private state stuffs
        private int _blueScore;
        private int _redScore;
        private bool _running;
        private readonly List<CTF_Sosig> _sosigs = new List<CTF_Sosig>();

        private readonly SosigAPI.SpawnOptions _spawnOptions = new SosigAPI.SpawnOptions
        {
            SpawnState = Sosig.SosigOrder.Assault,
            SpawnActivated = true,
            EquipmentMode = SosigAPI.SpawnOptions.EquipmentSlots.All,
            SpawnWithFullAmmo = true
        };

        private void Start()
        {
            UpdateScoreText();
        }

        public void ToggleGame()
        {
            if (_running)
            {
                EndGame();
                StartButtonText.text = "Start Game";
            }
            else
            {
                StartGame();
                StartButtonText.text = "Stop Game";
            }
        }

        private void StartGame()
        {
            // Register the death event and spawn all the Sosigs
            ResetGame();
            _running = true;
            GM.CurrentSceneSettings.SosigKillEvent += CurrentSceneSettingsOnSosigKillEvent;
            StartCoroutine(DoInitialSpawns());
        }

        private void EndGame()
        {
            // Unregister our death event and clear all the Sosigs
            GM.CurrentSceneSettings.SosigKillEvent -= CurrentSceneSettingsOnSosigKillEvent;
            foreach (var sosig in _sosigs)
                sosig.Sosig.ClearSosig();
            _running = false;
        }

        private void CurrentSceneSettingsOnSosigKillEvent(Sosig s)
        {
            // Make sure the sosig is managed by us
            var sosig = _sosigs.FirstOrDefault(x => x.Sosig == s);
            if (!sosig) return;

            // Start a coroutine to respawn this sosig
            StartCoroutine(RespawnSosig(sosig));
        }

        private void SpawnSosig(CTF_Team team)
        {
            _spawnOptions.IFF = (int) team;
            _spawnOptions.SosigTargetPosition = AttackPoints.GetRandom().position;
            Transform spawnPos;
            SosigEnemyID spawnId;

            if (team == CTF_Team.Red)
            {
                spawnPos = RedSpawns.GetRandom().transform;
                spawnId = RedTeam.GetRandom();
            }
            else
            {
                spawnPos = BlueSpawns.GetRandom().transform;
                spawnId = BlueTeam.GetRandom();
            }

            var sosig =
                SosigAPI.Spawn(IM.Instance.odicSosigObjsByID[spawnId], _spawnOptions, spawnPos.position,
                    spawnPos.rotation);
            var ctf = sosig.gameObject.AddComponent<CTF_Sosig>();
            _sosigs.Add(ctf);
            ctf.Sosig = sosig;
            ctf.Team = team;
        }

        private IEnumerator DoInitialSpawns()
        {
            var i = 0;
            while (i < Mathf.Max(RedTeamSize, BlueTeamSize))
            {
                if (i < RedTeamSize) SpawnSosig(CTF_Team.Red);
                if (i < BlueTeamSize) SpawnSosig(CTF_Team.Blue);
                i++;
                yield return new WaitForSeconds(2.5f);
            }
        }

        private IEnumerator RespawnSosig(CTF_Sosig sosig)
        {
            // Wait for 5 seconds then splode the Sosig
            yield return new WaitForSeconds(5f);
            sosig.Sosig.ClearSosig();
            _sosigs.Remove(sosig);

            // Wait another bit then respawn it
            yield return new WaitForSeconds(5f);

            // If the game was ended don't spawn a new one
            if (!_running) yield break;

            var sosigsLeft = _sosigs.Count(x => x.Team == sosig.Team);
            var teamSize = sosig.Team == CTF_Team.Red ? RedTeamSize : BlueTeamSize;
            if (sosigsLeft < teamSize) SpawnSosig(sosig.Team);
        }

        public void ResetGame()
        {
            _blueScore = 0;
            _redScore = 0;
            UpdateScoreText();

            if (RedFlag) RedFlag.ReturnFlag();
            if (BlueFlag) BlueFlag.ReturnFlag();
        }

        public void FlagCaptured(CTF_Flag flag)
        {
            if (flag.Team == CTF_Team.Red) _blueScore++;
            else _redScore++;
            UpdateScoreText();

            flag.ReturnFlag();
        }

        public void UpdateScoreText()
        {
            foreach (var text in ScoreTexts)
                text.text = "<color=red>" + _redScore + "</color> - <color=blue>" + _blueScore + "</color>";
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            foreach (var point in RedSpawns)
                Gizmos.DrawSphere(point.position, 0.15f);

            Gizmos.color = Color.blue;
            foreach (var point in BlueSpawns)
                Gizmos.DrawSphere(point.position, 0.15f);

            Gizmos.color = Color.green;
            foreach (var point in AttackPoints)
                Gizmos.DrawSphere(point.position, 0.15f);
        }
#endif
    }
}