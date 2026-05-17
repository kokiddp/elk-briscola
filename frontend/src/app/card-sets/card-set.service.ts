import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { Card } from '../features/game/game.models';
import {
  CardSet,
  CardSetManifest,
  PLACEHOLDER_MANIFEST,
  PLACEHOLDER_SET_ID,
  buildCardSet,
} from './card-set.models';

const CARD_SETS_ENDPOINT = '/api/v1/card-sets';
const ME_ENDPOINT = '/api/v1/me';

@Injectable({ providedIn: 'root' })
export class CardSetService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly manifestsSig = signal<readonly CardSetManifest[]>([PLACEHOLDER_MANIFEST]);
  private readonly activeSetIdSig = signal<string>(PLACEHOLDER_SET_ID);

  // Per (setId, card) pair we've already warned about, so a missing asset
  // doesn't spam the console.
  private readonly warned = new Set<string>();

  // Latches once we've loaded the catalog and seeded the active set from
  // the authenticated user's profile, so logout → login cycles don't
  // refetch on every effect tick.
  private bootstrapped = false;

  private readonly placeholder = buildCardSet(PLACEHOLDER_MANIFEST);

  readonly manifests = computed(() => this.manifestsSig());
  readonly activeSetId = computed(() => this.activeSetIdSig());
  readonly activeSet = computed<CardSet>(() => {
    const id = this.activeSetIdSig();
    const manifest = this.manifestsSig().find((m) => m.id === id);
    return manifest ? buildCardSet(manifest) : this.placeholder;
  });

  constructor() {
    // Bootstrap the catalog + active-set selection from the user's profile
    // the first time we observe an authenticated session. A subsequent
    // logout resets the latch so the next login re-bootstraps from the
    // (potentially different) user.
    effect(() => {
      const user = this.auth.currentUser();
      if (!user) {
        this.bootstrapped = false;
        return;
      }
      if (this.bootstrapped) {
        return;
      }
      this.bootstrapped = true;
      // Seed the active set from the user's stored preference *before* the
      // manifest fetch resolves so the first paint doesn't flash placeholder
      // assets while a different set is loading.
      if (user.activeCardSetId) {
        this.activeSetIdSig.set(user.activeCardSetId);
      }
      void this.loadManifests();
    });
  }

  /**
   * Sets the locally-active card set immediately (optimistic) and, when
   * the caller is authenticated, persists the choice via `PATCH /me`.
   * Reverts the local change if the request fails so the UI doesn't drift
   * from the server. Unauthenticated callers (spectators, auth screens)
   * still get the local switch — there's no server state to keep in sync.
   */
  async setActiveSet(id: string): Promise<void> {
    const previous = this.activeSetIdSig();
    if (previous === id) {
      return;
    }
    this.activeSetIdSig.set(id);
    if (!this.auth.currentUser()) {
      return;
    }
    try {
      await firstValueFrom(this.http.patch(ME_ENDPOINT, { activeCardSetId: id }));
    } catch (err) {
      this.activeSetIdSig.set(previous);
      throw err;
    }
  }

  /**
   * Fetch the installed card-set manifests from the backend. Safe to call
   * multiple times — later calls overwrite the cached list. Failures keep
   * the placeholder-only default so the UI never breaks.
   */
  async loadManifests(): Promise<void> {
    try {
      const list = await firstValueFrom(this.http.get<CardSetManifest[]>(CARD_SETS_ENDPOINT));
      const next = list.length === 0 ? [PLACEHOLDER_MANIFEST] : list;
      // Always ensure the placeholder entry exists so resolveFallback works.
      const withPlaceholder = next.some((m) => m.id === PLACEHOLDER_SET_ID)
        ? next
        : [...next, PLACEHOLDER_MANIFEST];
      this.manifestsSig.set(withPlaceholder);
    } catch {
      // Keep the seeded placeholder; UI continues to render schematics.
    }
  }

  /** Resolves the URL to use when the active set's asset 404s. */
  fallbackFrontUrl(card: Card, missingSetId: string): string {
    const key = `${missingSetId}:${card.suit}:${card.rank}`;
    if (!this.warned.has(key)) {
      this.warned.add(key);
      console.warn(
        `[card-sets] missing asset for ${card.suit}/${card.rank} in set "${missingSetId}"; falling back to placeholder`,
      );
    }
    return this.placeholder.resolveFront(card);
  }

  fallbackBackUrl(missingSetId: string): string {
    const key = `${missingSetId}:back`;
    if (!this.warned.has(key)) {
      this.warned.add(key);
      console.warn(
        `[card-sets] missing back asset in set "${missingSetId}"; falling back to placeholder`,
      );
    }
    return this.placeholder.resolveBack();
  }
}
