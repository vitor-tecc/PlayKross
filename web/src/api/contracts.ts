export interface ParticipantSnapshot { sessionId: string; nickname: string }
export interface StreamSnapshot { streamId: string; ownerSessionId: string; nickname: string; status: string }
export interface RoomSnapshot { roomId: string; revision: number; participants: ParticipantSnapshot[]; streams: StreamSnapshot[] }
export interface SessionGrant { roomId: string; sessionId: string; sessionToken: string; expiresAt: string; room: RoomSnapshot }
export interface SessionOverview { sessionId: string; expiresAt: string; room: RoomSnapshot }
export interface PlaybackGrant { streamId: string; path: string; whepUri: string; token: string; expiresAt: string }
