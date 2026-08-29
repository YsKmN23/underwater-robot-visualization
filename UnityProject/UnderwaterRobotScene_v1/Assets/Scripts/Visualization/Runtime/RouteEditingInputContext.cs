namespace UnderwaterRobotScene.Visualization.Runtime
{
    public static class RouteEditingInputContext
    {
        private static object routeEditorOwner;

        public static bool IsRouteEditorActive => routeEditorOwner != null;

        public static void SetRouteEditorActive(object owner, bool active)
        {
            if (owner == null)
                return;
            if (active)
            {
                routeEditorOwner = owner;
            }
            else if (ReferenceEquals(routeEditorOwner, owner))
            {
                routeEditorOwner = null;
            }
        }

        public static bool SelectionMayConsumePrimaryPointer()
        {
            return !IsRouteEditorActive;
        }

    }
}
