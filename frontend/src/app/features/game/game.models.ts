export type Suit = 'Bastoni' | 'Coppe' | 'Denari' | 'Spade';

export type Rank =
  | 'Asso'
  | 'Tre'
  | 'Re'
  | 'Cavallo'
  | 'Fante'
  | 'Sette'
  | 'Sei'
  | 'Cinque'
  | 'Quattro'
  | 'Due';

export interface Card {
  suit: Suit;
  rank: Rank;
}

export type GamePhase = 'Dealing' | 'Playing' | 'LastHand' | 'Finished';

export type GameMode = 'TwoPlayer' | 'FourPlayerTeams';

export interface PlayedCard {
  seatIndex: number;
  card: Card;
}

export interface GameOutcome {
  kind: string;
  winnerKey: number | null;
}

export interface PlayerInfo {
  userId: string;
  displayName: string;
  elo: number;
}

export interface RedactedStateForUser {
  gameId: string;
  mode: GameMode;
  phase: GamePhase;
  dealerSeat: number;
  leaderSeat: number;
  nextToPlaySeat: number;
  trickNumber: number;
  briscolaCard: Card;
  briscolaSuit: Suit;
  stockCount: number;
  handCountsBySeat: number[];
  myHand: Card[] | null;
  myPozzo: Card[] | null;
  currentTrick: PlayedCard[];
  seatScores: number[];
  outcome: GameOutcome | null;
  /** Calling player's seat. `null` for spectators. Optional in TS to stay
   *  forward-compatible with older snapshots; servers post-Phase-8-review
   *  always populate it. */
  mySeatIndex?: number | null;
  /** Positional, one entry per seat — display name + current Elo for
   *  each occupied seat, or null where the seat is empty. Refreshed
   *  on every snapshot the server pushes, and patched in-place on
   *  ranking-updated events as games finish. */
  seatPlayers?: (PlayerInfo | null)[];
}

export interface GameChatMessage {
  id: string;
  gameId: string;
  fromUserId: string;
  fromUserName: string;
  text: string;
  createdAt: string;
}

export type GameFinishReason = 'Normal' | 'ForfeitDisconnect' | 'ForfeitIdle';

export interface GameFinishedEvent {
  outcome: GameOutcome;
  seatScores: number[];
  reason: GameFinishReason;
}

export type InvalidMoveCode =
  | 'NotYourTurn'
  | 'CardNotInHand'
  | 'GameFinished'
  | 'WrongPhase'
  | 'PileViewNotAllowed'
  | 'SpectatorsCannotChat'
  | 'RateLimited';

export interface SeatDisconnectInfo {
  seatIndex: number;
  graceDeadlineUtc: string;
}

export function cardKey(card: Card): string {
  return `${card.suit}:${card.rank}`;
}
