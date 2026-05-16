export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export interface RankingDto {
  elo: number;
  wins: number;
  losses: number;
  draws: number;
  gamesPlayed: number;
  updatedAt: string;
}

export interface MeResponse {
  id: string;
  username: string;
  displayName: string;
  email: string;
  activeCardSetId: string;
  ranking: RankingDto;
}

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
  displayName?: string | null;
}

export interface LoginRequest {
  usernameOrEmail: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
