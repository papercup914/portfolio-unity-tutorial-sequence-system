using System;
using System.Collections.Generic;
using Data;
using STRAT.Client.Tutorial;
using STRAT.Stage;
using STRAT.UI;
using STRAT.UI.DebugUI;
using UnityEngine;

namespace STRAT
{
    public static class SequenceGroupMaker
    {
        public static IEnumerable<ISequencePlayingController> CreateTutorials(SequenceCategory currCategory)
        {
            if (currCategory == SequenceCategory.Prolog)
            {
                yield return new LinearSequencePlayingController(SequenceCategory.Prolog, OnNodePlay: OnNodePlay, OnFinish: OnTutorialNodeFinish);
                yield return new LinearSequencePlayingController(SequenceCategory.Startingpath, OnNodePlay: OnNodePlay, OnFinish: OnTutorialNodeFinish);
            }
            else if (currCategory == SequenceCategory.Startingpath)
            {
                yield return new LinearSequencePlayingController(SequenceCategory.Startingpath, OnNodePlay: OnNodePlay, OnFinish: OnTutorialNodeFinish);
            }

            foreach (var node in CreateNonSequenceNodes())
            {
                yield return node;
            }
        }

        public static IEnumerable<ISequencePlayingController> CreateNonSequenceNodes()
        {
            HashSet<int> groupIndex = new HashSet<int>();
            foreach (var data in NonSequenceGroupSheet.Data)
            {
                groupIndex.TryAdd(data.SequenceData.Group);
            }

            foreach (var group in groupIndex)
            {
                yield return new NonSequencePlayingController(group, OnFinish: OnNonSequenceNodeFinish);
            }
        }

        public static IEnumerable<ISequencePlayingController> CreateCampaignNode(int groupId)
        {
            yield return new CampaignSequencePlayingController(SequenceCategory.Prolog, groupId, OnCampaignNodeFinish);
        }

        public static IEnumerable<ISequencePlayingController> CreateCustomNodes(List<int> Kinds, Action<SequenceCategory> onFinishAction)
        {
            yield return new CustomSequencePlayingController(SequenceCategory.Campaign, Kinds, onFinishAction);
        }

        private static void OnNodePlay(SequenceCategory category, int kind)
        {
            TutorialUtils.SaveProgress(category, kind);
        }

        private static void OnTutorialNodeFinish(SequenceCategory category)
        {
            SequenceManager.Instance.FinishCurrentSequence();
            TouchBlock.Hide($"Sequence Stop Category: {category}");

            if (category == SequenceCategory.Prolog)
            {
                SequenceManager.Instance.SequencePlayingControllers.RemoveAll(x => x.Category == SequenceCategory.Prolog);
            }
            else if (category == SequenceCategory.Startingpath)
            {
                SequenceManager.Instance.SequencePlayingControllers.RemoveAll(x => x.Category == SequenceCategory.Prolog);
                SequenceManager.Instance.SequencePlayingControllers.RemoveAll(x => x.Category == SequenceCategory.Startingpath);
            }

            TutorialManager.Instance.PlayRemainSequence();
        }

        private static void OnCampaignNodeFinish(SequenceCategory category)
        {
            SequenceManager.Instance.FinishCurrentSequence();
            SequenceManager.Instance.SequencePlayingControllers.RemoveAll(x => x is CampaignSequencePlayingController controller && controller.Category == category);
            TouchBlock.Hide($"Sequence Stop Category: {category}");
            StageManager.ChangeMainStage(Stage.MainStage.OpenDataTerritory.GetCache(), _ => { });
        }

        private static void OnNonSequenceNodeFinish(int groupId)
        {
            SequenceManager.Instance.FinishCurrentSequence();
            TouchBlock.Hide($"Sequence Stop GroupId: {groupId}");
            TutorialUtils.SaveCompleteNonSequenceGroup(groupId);

            SequenceManager.Instance.SequencePlayingControllers.RemoveAll(x => x is NonSequencePlayingController controller && controller.Group == groupId);
            TutorialManager.Instance.PlayRemainSequence();
        }
    }
}