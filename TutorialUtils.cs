using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common.Protocol;
using Data;
using Networks;
using STRAT.Client.Sequence;
using STRAT.Client.Tutorial;
using STRAT.ServerData;
using STRAT.Stage;
using STRAT.UI;
using UnityEngine;

namespace STRAT.Client.Tutorial
{
    public static class TutorialUtils
    {
        public static void SaveProgress(SequenceCategory category, int kind)
        {
            var item = SequencePrologSheet.Find((x) => x.SequenceCategory == category && x.Kind == kind);
            if (item != null && item.SequenceData.SavePoint)
            {
                new Http2SendHelper(new SetLordTutorialRequest()
                {
                    Category = category,
                    Kind = kind
                }).Send<SetLordTutorialResponse>(response =>
                {
                    if (response.IsOk == false) Debug.Log(Color.red, $"[Sequence-TutorialManager] SaveProgress Failed {category}, {kind}");
                    else Debug.Log(Color.green, $"[Sequence-TutorialManager] SaveProgress {category}, {kind}");
                });
            }
        }

        public static async Task<bool> SaveProgressAsync(SequenceCategory category, int kind)
        {
            var item = SequencePrologSheet.Find((x) => x.SequenceCategory == category && x.Kind == kind);
            if (item != null && item.SequenceData.SavePoint)
            {
                var response = await new Http2SendHelper(new SetLordTutorialRequest()
                {
                    Category = category,
                    Kind = kind
                }).SendAsync<SetLordTutorialResponse>();

                if (response.IsOk == false)
                {
                    Debug.Log(Color.red, $"[Sequence-TutorialManager] SaveProgress Failed {category}, {kind}");
                    return false;
                }
                else
                {
                    Debug.Log(Color.green, $"[Sequence-TutorialManager] SaveProgress {category}, {kind}");
                    return true;
                }
            }
            return false;
        }

        public static void SaveCompleteNonSequenceGroup(int group)
        {
            if (LordHolder.Get.LordSetting.Setting.CompleteNonSequenceGroup.Contains(group) == false)
            {
                LordHolder.Get.LordSetting.Setting.CompleteNonSequenceGroup.Add(group);
                LordHolder.Get.LordSetting.SendSetLordSetting();
            }
        }

        public static int CurrentPlayingTutorialKind()
        {
            return TutorialManager.Instance.GetCurrentKind;
        }

        public static void ActiveContents(SequenceCategory category, int nodeKind)
        {
            if (category == SequenceCategory.Prolog) PrologActiveContents(nodeKind);
            else StartingPathActiveContents(nodeKind);
        }

        private static void PrologActiveContents(int nodeKind)
        {
            var list = new List<SequenceData>();
            foreach (var item in SequencePrologSheet.PrologActiveComponentData.Value)
            {
                if (item.Kind >= nodeKind) break;
                var index = list.FindIndex((x) => x.Type == item.SequenceData.Type && x.Value1 == item.SequenceData.Value1 && x.Value2 == item.SequenceData.Value2);
                if (index >= 0) list[index] = item.SequenceData;
                else list.Add(item.SequenceData);
            }
            foreach (var sequenceData in list)
            {
                ActiveComponentOfSequenceData(sequenceData);
            }
        }

        public static void ActiveComponentOfSequenceData(SequenceData sequenceData)
        {
            switch (sequenceData.Type)
            {
                case SequenceNodeType.ActiveComponent:
                    {
                        var dlgName = string.Empty;
                        var key = string.Empty;
                        var active = false;
                        if (!string.IsNullOrEmpty(sequenceData.Value1)) dlgName = sequenceData.Value1;
                        if (!string.IsNullOrEmpty(sequenceData.Value2)) key = sequenceData.Value2;
                        if (!string.IsNullOrEmpty(sequenceData.Value3)) bool.TryParse(sequenceData.Value3, out active);

                        var dlg = UIManager.Instance.FindUI(dlgName);
                        if (dlg != null)
                        {
                            var targetComp = dlg.GetComponent<SequenceTargetComponent>();
                            if (targetComp != null) targetComp.Active(key, active);
                        }
                        break;
                    }
            }
        }

        private static void StartingPathActiveContents(int nodeKind)
        {
            var list = new List<SequenceData>();
            foreach (var item in SequencePrologSheet.StartingPathActiveComponentData.Value)
            {
                if (item.Kind >= nodeKind) break;
                var index = list.FindIndex((x) => x.Type == item.SequenceData.Type && x.Value1 == item.SequenceData.Value1 && x.Value2 == item.SequenceData.Value2);
                if (index >= 0) list[index] = item.SequenceData;
                else list.Add(item.SequenceData);
            }

            foreach (var sequenceData in list)
            {
                switch (sequenceData.Type)
                {
                    case SequenceNodeType.ActiveComponent:
                        ActiveComponentOfSequenceData(sequenceData);
                        break;
                    case SequenceNodeType.ActiveInputIdleShortcut:
                        {
                            var active = false;
                            if (!string.IsNullOrEmpty(sequenceData.Value1)) bool.TryParse(sequenceData.Value1, out active);
                            TutorialManager.Instance.BornInputIdleShortcut = active;
                            break;
                        }
                }
            }
        }

        public static void MoveStage(SequenceNodeBase node, Action onStageComplete)
        {
            if (node == null)
            {
                onStageComplete?.Invoke();
                return;
            }

            StageType stageType = StageType.None;
            string loadingImage = string.Empty;
            bool stageExtraValue1 = false;
            bool stageExtraValue2 = false;

            if (node is SequenceNodeStage nodeStage)
            {
                stageType = nodeStage.StageType;
                stageExtraValue1 = nodeStage.StageExtraValue_IsPrologue;
                stageExtraValue2 = nodeStage.StageExtraValue_HideQuest;
                loadingImage = nodeStage.LoadingImage;
            }
            else if (node is SequenceNodeWaitStageChange nodeWaitStage)
            {
                stageType = nodeWaitStage.StageType;
            }

            var t = SequenceManager.SetStageTypeFromSequenceStageType(stageType);
            if (StageManager.GetCurrentStageType() == t)
            {
                onStageComplete?.Invoke();
                return;
            }

            if (stageType == StageType.Void)
            {
                var param = VoidStage.VoidStageMists.Cached;
                param.Clear();
                param.LoadingImageName = loadingImage;
                StageManager.ChangeStage(typeof(VoidStage), param, _ => { onStageComplete?.Invoke(); });
            }
            else if (stageType == StageType.Territory)
            {
                var param = MainStage.OpenDataTerritory.GetCache();
                param.LoadingImageName = loadingImage;
                StageManager.ChangeMainStage(param, _ => { onStageComplete?.Invoke(); });
            }
            else if (stageType == StageType.Lobby)
            {
                StageManager.ChangeLobby(new LobbyStageData()
                {
                    IsPrologue = stageExtraValue1,
                    QuestDlgHide = stageExtraValue2,
                    OnExitCallback = null
                }, _ => { onStageComplete?.Invoke(); });
            }
            else
            {
                onStageComplete?.Invoke();
            }
        }

        public static bool IsShopPopupMissionIconEnabled()
        {
            var kind = TutorialUtils.CurrentPlayingTutorialKind();
            if (kind > 8000 && kind < 9000) return false;
            if (kind > 9000 && kind < 9023) return false;
            return true;
        }
    }
}