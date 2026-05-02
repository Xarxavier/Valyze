import { useUi } from "../ui/UiContext";
import { Chat } from "./Chat";
import { Portfolio } from "./Portfolio";

/**
 * Two-pane home view: chat on the left, portfolio dashboard on the right.
 * Trade import lives on its own page now; the dashboard only shows
 * positions and quantities.
 */
export function Workspace() {
  const { portfolioReloadKey } = useUi();
  return (
    <div className="workspace">
      <div className="workspace-pane workspace-chat">
        <Chat reloadKey={portfolioReloadKey} />
      </div>
      <div className="workspace-pane workspace-dashboard">
        <Portfolio reloadKey={portfolioReloadKey} />
      </div>
    </div>
  );
}
