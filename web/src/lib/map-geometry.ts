// Projection adapted from core/world/World.cs at 1f755ca. LGPL-2.1-or-later.
import type { Position, Track } from '../generated/contracts.ts';
export interface Point { x: number; y: number }
export interface Camera { x: number; y: number; zoom: number }
export function project(p: Position): Point { return { x: 16 * (p.x + p.y), y: 8 * (p.y - p.x) - 16 * p.z }; }
export function unproject(p: Point, z: number): Point { return { x: p.x / 32 - (p.y + 16*z) / 16, y: p.x / 32 + (p.y + 16*z) / 16 }; }
export function toScreen(p: Position, camera: Camera, width: number, height: number): Point {
  const q = project(p); return { x: (q.x-camera.x)*camera.zoom+width/2, y: (q.y-camera.y)*camera.zoom+height/2 };
}
export function tileAt(p: Point, z: number, camera: Camera, width: number, height: number): Position {
  const q = unproject({ x: (p.x-width/2)/camera.zoom+camera.x, y: (p.y-height/2)/camera.zoom+camera.y },z);
  return { x: Math.floor(q.x+0.5), y: Math.floor(q.y+0.5), z };
}
export function zoomAt(camera: Camera, factor: number, p: Point, width: number, height: number): Camera {
  const zoom = Math.min(4, Math.max(0.5, camera.zoom*factor));
  return { x: camera.x+(p.x-width/2)*(1/camera.zoom-1/zoom), y: camera.y+(p.y-height/2)*(1/camera.zoom-1/zoom), zoom };
}
export function same(a: Position, b: Position) { return a.x===b.x && a.y===b.y && a.z===b.z; }
export const ports: Record<Track['shape'], readonly Point[]> = {
  eastWest: [{x:1,y:0},{x:-1,y:0}], northSouth: [{x:0,y:-1},{x:0,y:1}],
  curveSE: [{x:0,y:1},{x:1,y:0}], curveSW: [{x:0,y:1},{x:-1,y:0}],
  curveNE: [{x:0,y:-1},{x:1,y:0}], curveNW: [{x:0,y:-1},{x:-1,y:0}],
  junctionE: [{x:0,y:-1},{x:0,y:1},{x:1,y:0}], buffer: [{x:-1,y:0}],
};
export interface SpriteMask { width: number; height: number; mask: readonly string[] }
export function opaqueAt(sprite: SpriteMask, x: number, y: number) {
  const ix=Math.floor(x), iy=Math.floor(y);
  return ix>=0 && iy>=0 && ix<sprite.width && iy<sprite.height && sprite.mask[iy][ix]==='1';
}
export function visible(x: number,y: number,w: number,h: number,width: number,height: number) { return x+w>=0 && y+h>=0 && x<=width && y<=height; }
