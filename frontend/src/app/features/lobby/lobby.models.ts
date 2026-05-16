export type GameMode = 'TwoPlayer' | 'FourPlayerTeams';

export type GameStatus = 'Open' | 'Running' | 'Finished' | 'Abandoned';

export interface GameSummary {
  id: string;
  mode: GameMode;
  name: string;
  status: GameStatus;
  occupiedSeats: number;
  totalSeats: number;
  isPrivate: boolean;
  createdAt: string;
  startedAt: string | null;
}

export interface CreateGameRequest {
  mode: GameMode;
  name: string;
  isPrivate: boolean;
  password?: string | null;
}

export interface JoinGameRequest {
  password?: string | null;
}

export interface GameDetail {
  id: string;
  mode: GameMode;
  name: string;
  status: GameStatus;
  isPrivate: boolean;
  createdAt: string;
  startedAt: string | null;
  endedAt: string | null;
  seats: (string | null)[];
}

export interface LobbyChatMessage {
  id: string;
  fromUserId: string;
  fromUserName: string;
  text: string;
  createdAt: string;
}
