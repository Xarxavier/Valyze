import { Positions } from "./Positions";

interface Props {
  reloadKey: number;
}

export function Portfolio({ reloadKey }: Props) {
  return (
    <div className="card portfolio">
      <h2>Your portfolio</h2>
      <Positions reloadKey={reloadKey} />
    </div>
  );
}
