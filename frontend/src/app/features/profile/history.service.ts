import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatchHistoryPage } from '../../core/models';

const API_PREFIX = '/api/v1';

@Injectable({ providedIn: 'root' })
export class MatchHistoryService {
  private readonly http = inject(HttpClient);

  loadPage(page: number, pageSize: number): Promise<MatchHistoryPage> {
    const params = new HttpParams()
      .set('page', String(Math.max(1, page)))
      .set('size', String(Math.max(1, pageSize)));
    return firstValueFrom(this.http.get<MatchHistoryPage>(`${API_PREFIX}/me/history`, { params }));
  }
}
