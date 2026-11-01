import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_URL } from './api.config';
import { CroppedAsset, DesignDocument, GeneratedProject, LayoutMode, MotionLevel, PaletteColor } from './models';

@Injectable({ providedIn: 'root' })
export class DesignApi {
  private http = inject(HttpClient);

  /** Image in, structured design description (JSON) out. This is the only call that uses the AI. */
  analyze(image: File, palette: PaletteColor[] = [], part?: number, parts?: number): Observable<DesignDocument> {
    const form = new FormData();
    form.append('image', image);
    if (palette.length > 0) form.append('palette', JSON.stringify(palette));
    if (part && parts && parts > 1) {
      form.append('part', String(part));
      form.append('parts', String(parts));
    }
    return this.http.post<DesignDocument>(`${API_URL}/design/analyze`, form);
  }

  /** Design JSON in, Angular files and a standalone preview out. */
  generate(design: DesignDocument, motion: MotionLevel = 'subtle', layout: LayoutMode = 'measured'): Observable<GeneratedProject> {
    return this.http.post<GeneratedProject>(`${API_URL}/design/generate?motion=${motion}&layout=${layout}`, design);
  }

  /** Same as generate, but as a ZIP file. The cropped pictures are sent along so they can be packed into it. */
  download(design: DesignDocument, assets: CroppedAsset[], motion: MotionLevel = 'subtle', layout: LayoutMode = 'measured'): Observable<Blob> {
    const body = { design, assets: assets.map((a) => ({ fileName: a.fileName, base64: a.base64 })), motion, layout };
    return this.http.post(`${API_URL}/design/download`, body, { responseType: 'blob' });
  }
}
