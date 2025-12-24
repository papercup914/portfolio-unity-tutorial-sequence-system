using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.Misc.Events;
using Common.Protocol;
using Data;
using Networks;
using STRAT.Client.Sequence;
using STRAT.ServerData;
using STRAT.Stage;
using STRAT.UI;
using STRAT.UI.DebugUI;
using UnityEngine;

namespace STRAT.Client.Tutorial
{
    public partial class TutorialManager : MonoBehaviourSingleton<TutorialManager>
    {
        private bool _initialized = false;
        public bool Replay { get; private set; } = false;
        public bool NowPlaying = false;

        private SequenceCategory _currCategory = SequenceCategory.Prolog;
        private int _currentKind = 0;

        public bool BornInputIdleShortcut { get; set; }
        public bool ActiveInputIdleShortcut { get; set; }

        public List<string> NodeLogs = new List<string>();
        public int GetCurrentKind => _currentKind;

        public void TutorialFinish()
        {
            Release();
            SequenceManager.Instance.Stop();
            StageManager.ChangeMainStage(Stage.MainStage.OpenDataTerritory.GetCache(), _ => { });
        }

        public void Stop()
        {
            Release();
            SequenceManager.Instance.Stop();
        }

        public void Release()
        {
            EventAggregator.Instance.Unsubscribe<UpdateLordContentsValueMessage>(OnUpdateLordContentValueMessage);
            _initialized = false;
            NowPlaying = false;
            _currCategory = SequenceCategory.None;
            _currentKind = 0;
            ActiveInputIdleShortcut = false;
        }

        public bool CheckTutorialComplete()
        {
            return ExtractTutorialStep(out _currCategory, out _currentKind) == false;
        }

        public bool GetActiveInputIdleShortcut()
        {
            return BornInputIdleShortcut && ActiveInputIdleShortcut && TouchBlock.Show == false;
        }

        public override void Initialize()
        {
            if (_initialized) return;

            EventAggregator.Instance.Unsubscribe<UpdateLordContentsValueMessage>(OnUpdateLordContentValueMessage);
            EventAggregator.Instance.Subscribe<UpdateLordContentsValueMessage>(OnUpdateLordContentValueMessage);

            var finished = (ExtractTutorialStep(out _currCategory, out _currentKind) == false);
            var nonSequenceFinished = IsCompleteNonSequenceGroup();

            if (finished && nonSequenceFinished)
            {
                _initialized = true;
                return;
            }

            SequenceManager.Instance.Initialize();
            IEnumerable<ISequencePlayingController> sequences = null;
#if TUTORIAL_TEST_TERMINATION
            sequences = TestTutorialMaker.CreateTutorialTerminationSequenceNodes();
#else
            sequences = SequenceGroupMaker.CreateTutorialSequenceNodes(_currCategory);
#endif
            SequenceManager.Instance.SequencePlayingControllers.AddRange(sequences);

            NowPlaying = false;
            _initialized = true;
            ActiveInputIdleShortcut = false;
            Replay = false;
        }

        private bool IsCompleteNonSequenceGroup()
        {
            var nonSeqCount = NonSequenceGroupSheet.Data.Select(x => x.SequenceData.Group).Distinct().Count();
            var completeCount = LordHolder.Get.LordSetting.Setting.CompleteNonSequenceGroup.Count;
            return completeCount >= nonSeqCount;
        }

        public bool ExtractTutorialStep(out SequenceCategory outCategory, out int outKind)
        {
            LordHolder.Get.CustomContents.TryGetValue(LordContentsCustomType.Tutorial, out var lordContentsValue);
            return LordContentToTutorialValue(lordContentsValue, out outCategory, out outKind);
        }

        private void OnUpdateLordContentValueMessage(UpdateLordContentsValueMessage message)
        {
            var tutorialValue = message.Values.Find(x => x.Type == LordContentsCustomType.Tutorial);
            if (tutorialValue != null)
            {
                LordContentToTutorialValue(tutorialValue, out _currCategory, out _currentKind);
            }
        }

        private bool LordContentToTutorialValue(LordContentsValue lordContentsValue, out SequenceCategory outCategory, out int outKind)
        {
            if (lordContentsValue != null)
            {
                var step = lordContentsValue.ToObject<TutorialStep>(LordHolder.GetUserID);
                if (step != null)
                {
                    outCategory = (SequenceCategory)step.Category;
                    var lastNode = SequencePrologSheet.Data.OrderBy(x => x.Kind).Last();

                    if (Enum.IsDefined(typeof(SequenceCategory), step.Category) == false)
                    {
                        outCategory = SequenceCategory.Prolog;
                    }

                    if (outCategory == SequenceCategory.None)
                    {
                        outKind = lastNode.Kind;
                        return false;
                    }

                    if (step.Kind >= lastNode.Kind)
                    {
                        outKind = lastNode.Kind;
                        return false;
                    }

                    var findLast = SequencePrologSheet.FindAll((x) => x.Kind <= step.Kind && x.SequenceData.SavePoint).LastOrDefault();
                    var kind = findLast?.Kind ?? step.Kind;
                    outKind = kind;
                }
                else
                {
                    outCategory = SequenceCategory.Prolog;
                    outKind = SequencePrologSheet.FindAll((x) => x.SequenceCategory == _currCategory).FirstOrDefault()?.Kind ?? 1;
                }
            }
            else
            {
                outCategory = SequenceCategory.Prolog;
                outKind = SequencePrologSheet.FindAll((x) => x.SequenceCategory == _currCategory).FirstOrDefault()?.Kind ?? 1;
            }
            return true;
        }

        public void PlayRemainSequence()
        {
            var remainSequence = GetRemainLinearSequenceManual();
            if (remainSequence != null)
            {
                _currCategory = remainSequence.Category;
                if (remainSequence.CurrentNode != null)
                {
                    _currentKind = remainSequence.CurrentNode.Kind;
                }
                else
                {
                    _currentKind = remainSequence.NodeList.First()?.Kind ?? (int)SequenceCategory.None;
                }
                TutorialUtils.SaveProgress(_currCategory, _currentKind);
                Play();
            }
            else
            {
                _currCategory = SequenceCategory.None;
                _currentKind = (int)SequenceCategory.None;
                TutorialUtils.SaveProgress(_currCategory, _currentKind);
                NowPlaying = false;
            }
        }

        public ISequencePlayingController GetRemainLinearSequenceManual()
        {
            foreach (var controller in SequenceManager.Instance.SequencePlayingControllers)
            {
                if (controller is LinearSequencePlayingController)
                {
                    if (controller.Category == SequenceCategory.Startingpath) return controller;
                }
            }
            return null;
        }

        public void TutorialPlayStart()
        {
            ExtractTutorialStep(out _currCategory, out _currentKind);
            PlayWithPrepare();
        }

        public void PlayWithPrepare()
        {
            TutorialUtils.ActiveContents(_currCategory, _currentKind);
            SequenceNodeBase stageNode = null;
            var controller = SequenceManager.Instance.SequencePlayingControllers.Find((x) => x.Category == _currCategory);
            if (controller?.NodeList == null)
            {
                Debug.LogWarning("Tutorial Already Finished!");
                return;
            }

            foreach (var item in controller.NodeList)
            {
                if (item.Kind <= _currentKind)
                {
                    if (item.Data.Type == SequenceNodeType.Stage || item.Data.Type == SequenceNodeType.WaitStageChange)
                    {
                        stageNode = item;
                    }
                }
            }

            TutorialUtils.MoveStage(stageNode, () =>
            {
                PrepareBeforePlay(_currCategory, _currentKind);
                Play();
            });
        }

        public void Play()
        {
            SequenceManager.Instance.SetCurrentSequenceToLinear(_currCategory);
            SequenceManager.Instance.StartSequence(_currentKind);
            NowPlaying = true;
        }

        public void PlayCampaign()
        {
            _currCategory = SequenceCategory.Campaign;
            _currentKind = 1;
            SequenceManager.Instance.SetCurrentSequenceToLinear(_currCategory);
            SequenceManager.Instance.StartSequence();
            NowPlaying = true;
        }

        private static void PrepareBeforePlay(SequenceCategory category, int kind)
        {
            PrepareAudioOff();
            var currentSheet = SequencePrologSheet.Find(kind);
            if (currentSheet.SequenceData.PrepareGroup == 0) return;

            var currentController = SequenceManager.Instance.SequencePlayingControllers.Find(x => x is LinearSequencePlayingController && x.Category == category);
            if (currentController != null && currentSheet != null)
            {
                foreach (var item in currentController.NodeList.Where(x => x.Kind < kind && x.Data.PrepareGroup == currentSheet.SequenceData.PrepareGroup))
                {
                    if (item.Data.PrepareOn && item.IsImplementNeedPrepareForPlay)
                    {
                        item.PrepareForPlay();
                    }
                }
            }
        }

        private static void PrepareAudioOff()
        {
            AudioManager.Instance.StopAll(AudioSourceType.Sfx);
            AudioManager.Instance.StopAll(AudioSourceType.Bgm);
            AudioManager.Instance.StopAll(AudioSourceType.Amb);
            AudioManager.Instance.StopAll(AudioSourceType.Cv);
            AudioManager.Instance.StopAll(AudioSourceType.SfxUI);
        }

        public void TestPlayByKind(SequenceCategory category, int kind)
        {
            Release();
            Initialize();
            _currCategory = category;
            _currentKind = kind;
            Play();
        }
    }
}