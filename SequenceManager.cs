using Common.Misc.Events;
using Data;
using STRAT.Client;
using STRAT.Client.Sequence;
using STRAT.Field;
using STRAT.ServerData;
using STRAT.Stage;
using STRAT.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace STRAT
{
    public class SequenceManager : MonoBehaviourSingleton<SequenceManager>
    {
        public ISequencePlayingController CurrentSequencePlayingController { get; private set; }

        public delegate void OnPrepareForPlay(bool success);
        public List<ISequencePlayingController> SequencePlayingControllers { get; } = new List<ISequencePlayingController>();

        private Coroutine _playCoroutine;
        private readonly HashSet<string> _openedDlgList = new HashSet<string>();

        private readonly Lazy<Dictionary<int, string>> _nonSequenceGroupCondition = new(() =>
        {
            var result = new Dictionary<int, string>();
            result.Clear();
            result.Add(3, "QuestCondition:500707:CompleteRewarded");
            result.Add(5, "BuildingLevel:Headquarter:8");
            result.Add(7, "QuestCondition:500809:CompleteRewarded");
            return result;
        });

        public override void Initialize()
        {
            Release();
            EventAggregator.LSubscribe<UIManager.FinishUIClose>(OnUICloseEvent);
        }

        public void Release()
        {
            CurrentSequencePlayingController = null;
            SequencePlayingControllers.Clear();
            _openedDlgList.Clear();
            EventAggregator.LUnsubscribe<UIManager.FinishUIClose>(OnUICloseEvent);
        }

        public void Clear(SequenceCategory category)
        {
            SequencePlayingControllers.RemoveAll(x => x.Category == category);
        }

        public void SetCurrentSequenceToLinear(SequenceCategory category)
        {
            CurrentSequencePlayingController?.PauseNode();

            foreach (var sequencePlayingController in SequencePlayingControllers)
            {
                if (sequencePlayingController.Category == category)
                {
                    CurrentSequencePlayingController = sequencePlayingController;
                    return;
                }
            }
            Debug.LogError("Play Node Not Exist!");
        }

        public void StartSequence(int kind = 0)
        {
            SequenceManagerExtension.PrintLog($"Start Order : {kind.ToString()}");
            if (kind > 0)
            {
                CurrentSequencePlayingController?.Jump(kind);
            }
            PlayNode();
        }

        public void PlayNonSequenceGroup(int group)
        {
            CurrentSequencePlayingController?.PauseNode();
            foreach (var sequencePlayingController in SequencePlayingControllers)
            {
                if (sequencePlayingController is NonSequencePlayingController nonSequenceController)
                {
                    if (nonSequenceController.Group == group)
                    {
                        CurrentSequencePlayingController = nonSequenceController;
                        StartSequence();
                        return;
                    }
                }
            }
        }

        public bool CheckNonSequenceGroupCondition(int group)
        {
            foreach (var c in _nonSequenceGroupCondition.Value)
            {
                if (c.Key == group) return CheckPlayCondition(c.Value);
            }
            return false;
        }

        public bool CheckNonSequenceGroupComplete(int group = 0)
        {
            if (group == 0)
            {
                bool complete = true;
                List<int> groupIndex = new List<int>();
                foreach (var index in groupIndex)
                {
                    if (LordHolder.Get.LordSetting.Setting.CompleteNonSequenceGroup.Contains(group) == false)
                    {
                        complete = false;
                        break;
                    }
                }
                return complete;
            }
            else
            {
                return LordHolder.Get.LordSetting.Setting.CompleteNonSequenceGroup.Contains(group);
            }
        }

        public bool CheckPlayCondition(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return true;

            var split = condition.Split(':');
            SequenceConditionType type;
            BuildingKind _buildingKind;
            WorkType _workType;
            int _puzzleIndex;
            List<int> _questKinds = new List<int>();
            int _targetValue;
            SequenceNodeCondition.QuestState _questState = SequenceNodeCondition.QuestState.None;
            StageType stageType = StageType.None;

            Enum.TryParse(split[0], true, out type);

            switch (type)
            {
                case SequenceConditionType.Stage:
                    Enum.TryParse(split[1], false, out stageType);
                    if (stageType == StageType.Territory) return FieldManager.IsTerritory();
                    break;

                case SequenceConditionType.BuildingLevel:
                    Enum.TryParse(split[1], false, out _buildingKind);
                    int.TryParse(split[2], out _targetValue);
                    var building = LordHolder.Get.CurrentCity.Buildings.GetBuilding(_buildingKind);
                    if (building != null) return building.Level >= _targetValue;
                    break;

                case SequenceConditionType.BuildingWorkType:
                    Enum.TryParse(split[1], false, out _buildingKind);
                    Enum.TryParse(split[2], false, out _workType);
                    var works = LordHolder.Get.WorkContainer.Works.Values.Where(x => x.WorkType == _workType).ToList();
                    if (works.Count > 0)
                    {
                        foreach (var w in works)
                        {
                            if (w.GetWorkBuilding() == _buildingKind) return true;
                        }
                    }
                    break;

                case SequenceConditionType.PuzzleClear:
                    int.TryParse(split[1], out _puzzleIndex);
                    int.TryParse(split[2], out _targetValue);
                    {
                        var settingData = LordHolder.Get.DetailSettingDatas.GetDetailSettingData(_puzzleIndex);
                        return settingData?.IsReceive == true;
                    }

                case SequenceConditionType.QuestCondition:
                    int.TryParse(split[1], out var questKind);
                    _questKinds.Add(questKind);
                    Enum.TryParse(split[2], out _questState);
                    bool result = true;
                    foreach (var kind in _questKinds)
                    {
                        var sheet = QuestSheet.Find(kind);
                        if (sheet == null) continue;
                        var quest = LordHolder.Get.UserQuest.GetQuest(sheet.Kind);
                        if (quest == null) continue;

                        if (_questState == SequenceNodeCondition.QuestState.Rewarded) result &= quest.Complete && quest.Reward;
                        else if (_questState == SequenceNodeCondition.QuestState.Complete) result &= quest.Complete && (quest.Reward == false);
                        else if (_questState == SequenceNodeCondition.QuestState.CompleteRewarded) result &= quest.Complete || quest.Reward;
                    }
                    return result;
            }
            return false;
        }

        private void PlayNode()
        {
            StopRoutine();
            _playCoroutine = StartCoroutine(PlayNodeCoroutine());
        }

        private void StopRoutine()
        {
            if (_playCoroutine != null)
            {
                StopCoroutine(_playCoroutine);
                _playCoroutine = null;
            }
        }

        public void Stop()
        {
            StopRoutine();
            CurrentSequencePlayingController?.Stop();
            UIManager.CloseUI<BlackOutDlg>();
            ActiveTouchBlock(false, $"SequenceManager.Stop, {CurrentSequencePlayingController?.CurrentNode?.Kind ?? 0}");
            UIRootMono.Instance.ClearFade();
            if (StageManager.TryGetCurrentStage(out Stage.MainStage getMainStage))
            {
                getMainStage.BlockController.ReleaseBlock();
            }
            Release();
            SequenceManagerExtension.PrintLog("Reset");
        }

        private IEnumerator PlayNodeCoroutine()
        {
            if (CurrentSequencePlayingController == null)
            {
                SequenceManagerExtension.PrintLog("No Sequence Playing Controller found. Cannot play node.", "red");
                yield break;
            }
            yield return CurrentSequencePlayingController.PlayNodeCoroutine();
        }

        public void FinishCurrentSequence()
        {
            CurrentSequencePlayingController = null;
        }

        public static void ActiveTouchBlock(bool active, string caller)
        {
            if (active) TouchBlock.Show(caller);
            else TouchBlock.Hide(caller);
        }

        public static Type SetStageTypeFromSequenceStageType(StageType stageType)
        {
            switch (stageType)
            {
                case StageType.Void: return typeof(VoidStage);
                case StageType.Territory: return typeof(MainStage);
                case StageType.Lobby: return typeof(LobbyStage);
                default: return default;
            }
        }

        private bool WaitNode(SequenceNodeType type)
        {
            switch (type)
            {
                case SequenceNodeType.WaitChapter:
                case SequenceNodeType.WaitActiveComponent:
                case SequenceNodeType.WaitMainStageSeamlessLayer:
                case SequenceNodeType.WaitWorkState:
                    return true;
            }
            return false;
        }

        public void Skip(bool next = false)
        {
            var node = CurrentSequencePlayingController?.CurrentNode;
            if (node == null)
            {
                SequenceManagerExtension.PrintLog($"Node is null.");
                return;
            }

            if (WaitNode(node.Data.Type)) return;

            node.Stop();
            node.Complete();
            var kind = 0;

            if (!next)
            {
                kind = GetSkipNodeKind(node.Data);
                if (VersionChecker.Instance.IsReviewServer() == true) kind = 1000;
                if (kind > 0) StartSequence(kind);
                ClearSkippedSingleGame(node.Kind, kind);
            }
        }

        private void ClearSkippedSingleGame(int currNodeKind, int skipPointNodeKind)
        {
            foreach (var n in CurrentSequencePlayingController.NodeList)
            {
                if (n.Kind <= currNodeKind) continue;
                if (skipPointNodeKind <= n.Kind) break;
                if (n.Data.Type != SequenceNodeType.Stage) continue;

                if (n is SequenceNodeStage nodeStage && nodeStage.StageType == StageType.SingleGame)
                {
                    var stage = new Data.MiniGameStage()
                    {
                        Kind = n.Kind,
                        Status = MiniGameStatus.Complete,
                        Created = DateTime.UtcNow,
                        Updated = DateTime.UtcNow,
                    };
                    LordHolder.Get.LordSetting.UpdateMiniGameStage(stage);
                }
            }
        }

        public void SkipKind(int kind)
        {
            var node = CurrentSequencePlayingController?.CurrentNode;
            if (node == null) return;

            node.Stop();
            node.Complete();
            StartSequence(kind);
        }

        public bool SkipAuto()
        {
            if (CurrentSequencePlayingController == null) return false;

            if (CheckEnableSkip())
            {
                Skip();
                return true;
            }
            return false;
        }

        private int GetSkipNodeKind(SequenceData sequenceData)
        {
            if (sequenceData.Skip == SkipType.Point) return sequenceData.SkipPoint;
            if (sequenceData.Skip == SkipType.Auto) return sequenceData.SkipPoint;
            return 0;
        }

        public bool CheckEnableSkip()
        {
            var node = CurrentSequencePlayingController.CurrentNode;
            if (node != null)
            {
                if (node.Data.Type == SequenceNodeType.TimelinePlayer) return true;
                else if (node.Data.Type == SequenceNodeType.DialogOpen)
                {
                    if (node.Data.Value1 == "DialogueSystemV2Dlg" || node.Data.Value1 == "ContentOpenScreenDlg") return true;
                }
                else
                {
                    if (StageManager.Instance.CurrentStage.GetType() == typeof(DefenseGameStage)) return true;
                }
            }
            return false;
        }

        public void AddOpenedDlg(string dlgName)
        {
            _openedDlgList.Add(dlgName);
        }

        private void OnUICloseEvent(UIManager.FinishUIClose closeMessage)
        {
            _openedDlgList.Remove(closeMessage.DlgName);
        }

        public bool IsBlockInput()
        {
            var exitPopup = UIManager.Instance.FindUI<ApplicationExitPopupDlg>();
            if (exitPopup != null) return false;

            var dlgPass = _openedDlgList.IsNullOrEmpty();
            if (dlgPass == false) return true;

            return false;
        }
    }
}