using UnityEngine;

namespace FactionColonies
{
    /// <summary>
    /// Defines an interface that WorldObjectComps can implement in order to add a new overview tab to the settlement window.
    /// <para>This must be implemented by a WorldObjectComp. It will not be invoked otherwise.</para>
    /// </summary>
    public interface ISettlementWindowOverview
    {
        void PreOpenWindow(WorldSettlementFC settlement);
        void OnTabSwitch();
        void DrawOverviewTab(Rect boundingBox);
        void PostCloseWindow();
        string OverviewTabName();
    }
}
