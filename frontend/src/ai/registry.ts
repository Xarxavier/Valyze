import { ClaudeCodeVendor } from "./ClaudeCodeVendor";
import type { AiVendor } from "./types";

/**
 * Registered AI vendors keyed by id. The Chat UI iterates this list to
 * populate its vendor selector. Adding a vendor = importing it and pushing.
 */
export const aiVendors: AiVendor[] = [new ClaudeCodeVendor()];

export function getVendor(id: string): AiVendor | undefined {
  return aiVendors.find((v) => v.id === id);
}

export const defaultVendorId = aiVendors[0]?.id ?? "claude-code";
