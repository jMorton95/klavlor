// builder.js - DAG Builder Canvas Interactions

(function () {
    let dragState = null;
    let connectState = null;
    let panState = null;
    let annotationDragState = null;
    let regionDragState = null;
    let resizeState = null;
    var selectedGroupIds = new Set();

    // --- Helpers ---

    function getAntiforgeryToken() {
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function expandCanvasForElement(el, zoom) {
        var canvas = document.getElementById('builder-canvas');
        if (!canvas) return;
        var innerEl = canvas.firstElementChild;
        if (!innerEl) return;
        var padding = 300;
        var ex = parseFloat(el.style.left) || 0;
        var ey = parseFloat(el.style.top) || 0;
        var rect = el.getBoundingClientRect();
        var neededW = ex + rect.width / zoom + padding;
        var neededH = ey + rect.height / zoom + padding;
        if (neededW > parseFloat(innerEl.style.minWidth))
            innerEl.style.minWidth = neededW + 'px';
        if (neededH > parseFloat(innerEl.style.minHeight))
            innerEl.style.minHeight = neededH + 'px';
        var svg = innerEl.querySelector('svg');
        if (svg) {
            svg.style.minWidth = innerEl.style.minWidth;
            svg.style.minHeight = innerEl.style.minHeight;
        }
    }

    function calculateBezierPath(x1, y1, x2, y2) {
        var dx = Math.abs(x2 - x1);
        var dy = Math.abs(y2 - y1);

        if (dx >= dy) {
            var cpOffset = Math.min(Math.max(dx * 0.4, 20), dx / 2);
            return `M ${x1} ${y1} C ${x1 + cpOffset} ${y1}, ${x2 - cpOffset} ${y2}, ${x2} ${y2}`;
        } else {
            var cpOffset = Math.min(Math.max(dy * 0.4, 20), dy / 2);
            var cpDir = y2 > y1 ? 1 : -1;
            return `M ${x1} ${y1} C ${x1} ${y1 + cpOffset * cpDir}, ${x2} ${y2 - cpOffset * cpDir}, ${x2} ${y2}`;
        }
    }

    // --- Orthogonal routing ---
    //
    // Each edge is drawn as a sequence of axis-aligned segments connected by
    // small rounded corners. The first and last segments are perpendicular
    // "stubs" pointing away from the group; the middle segment(s) thread
    // between obstacles.

    var STUB_LENGTH = 30;          // px between port and first turn
    var CORNER_RADIUS = 8;         // px rounding at each turn
    var OBSTACLE_PADDING = 6;      // px halo around groups for clearance

    function getSideNormal(side) {
        if (side === 'right') return { x: 1, y: 0 };
        if (side === 'left') return { x: -1, y: 0 };
        if (side === 'bottom') return { x: 0, y: 1 };
        return { x: 0, y: -1 };
    }

    // Returns true when an axis-aligned segment from a to b enters rect's
    // interior (touching the boundary does not count).
    function segmentIntersectsRect(ax, ay, bx, by, rect) {
        var minSegX = Math.min(ax, bx), maxSegX = Math.max(ax, bx);
        var minSegY = Math.min(ay, by), maxSegY = Math.max(ay, by);
        var rx1 = rect.x, rx2 = rect.x + rect.w;
        var ry1 = rect.y, ry2 = rect.y + rect.h;
        // Overlap of bounding boxes (strict for the dimension the segment spans)
        if (maxSegX <= rx1 || minSegX >= rx2) return false;
        if (maxSegY <= ry1 || minSegY >= ry2) return false;
        return true;
    }

    function waypointsCrossAny(waypoints, obstacles) {
        for (var i = 0; i < waypoints.length - 1; i++) {
            var a = waypoints[i], b = waypoints[i + 1];
            for (var j = 0; j < obstacles.length; j++) {
                if (segmentIntersectsRect(a.x, a.y, b.x, b.y, obstacles[j])) return true;
            }
        }
        return false;
    }

    // Build the obstacle list for an edge — every group except its two
    // endpoints, expanded by a small padding so curves don't graze borders.
    function buildObstacleList(allRects, fromGid, toGid) {
        var out = [];
        for (var i = 0; i < allRects.length; i++) {
            var r = allRects[i];
            if (r.gid === fromGid || r.gid === toGid) continue;
            out.push({
                x: r.x - OBSTACLE_PADDING,
                y: r.y - OBSTACLE_PADDING,
                w: r.w + OBSTACLE_PADDING * 2,
                h: r.h + OBSTACLE_PADDING * 2
            });
        }
        return out;
    }

    // Returns true if every segment in the candidate waypoints list is
    // axis-aligned and the segments collectively connect p0 to p1.
    function tryRoute(waypoints, obstacles) {
        return !waypointsCrossAny(waypoints, obstacles);
    }

    // Z-route along the X axis: vertical-horizontal-vertical or
    // horizontal-vertical-horizontal connector through a single midpoint.
    function zRouteX(q0, q1) {
        var mid = (q0.x + q1.x) / 2;
        return [q0, { x: mid, y: q0.y }, { x: mid, y: q1.y }, q1];
    }
    function zRouteY(q0, q1) {
        var mid = (q0.y + q1.y) / 2;
        return [q0, { x: q0.x, y: mid }, { x: q1.x, y: mid }, q1];
    }

    // Sweep the midpoint of a Z-route along its routing axis until the middle
    // segment clears every obstacle. Returns null if no clear position exists.
    function clearedZRoute(q0, q1, obstacles, axis) {
        var candidates = [];
        // Natural midpoint first, then increasingly off-centre alternatives.
        if (axis === 'x') {
            var lo = Math.min(q0.x, q1.x), hi = Math.max(q0.x, q1.x);
            var natural = (q0.x + q1.x) / 2;
            for (var step = 0; step <= 8; step++) {
                var range = (hi - lo) / 2;
                var off = (step / 8) * range;
                candidates.push(natural + off);
                if (step > 0) candidates.push(natural - off);
            }
            for (var i = 0; i < candidates.length; i++) {
                var m = candidates[i];
                if (m <= lo + 1 || m >= hi - 1) continue;
                var wp = [q0, { x: m, y: q0.y }, { x: m, y: q1.y }, q1];
                if (tryRoute(wp, obstacles)) return wp;
            }
            return null;
        } else {
            var loY = Math.min(q0.y, q1.y), hiY = Math.max(q0.y, q1.y);
            var naturalY = (q0.y + q1.y) / 2;
            for (var stepY = 0; stepY <= 8; stepY++) {
                var rangeY = (hiY - loY) / 2;
                var offY = (stepY / 8) * rangeY;
                candidates.push(naturalY + offY);
                if (stepY > 0) candidates.push(naturalY - offY);
            }
            for (var k = 0; k < candidates.length; k++) {
                var my = candidates[k];
                if (my <= loY + 1 || my >= hiY - 1) continue;
                var wpY = [q0, { x: q0.x, y: my }, { x: q1.x, y: my }, q1];
                if (tryRoute(wpY, obstacles)) return wpY;
            }
            return null;
        }
    }

    // L-route: one corner, used when the two side normals are perpendicular.
    function lRoute(q0, q1, side0, side1) {
        var n0 = getSideNormal(side0), n1 = getSideNormal(side1);
        // The corner sits at the intersection of q0's stub line and q1's stub line.
        // n0 is horizontal ⇒ corner.x = q1.x, corner.y = q0.y. And vice versa.
        if (n0.x !== 0) return [q0, { x: q1.x, y: q0.y }, q1];
        return [q0, { x: q0.x, y: q1.y }, q1];
    }

    // U-route: both ports exit the same side. The connecting segment sits
    // further out along the side normal.
    function uRoute(q0, q1, side, p0, p1, fromRect, toRect) {
        var extra = STUB_LENGTH;
        if (side === 'right') {
            var x = Math.max(q0.x, q1.x) + extra;
            return [q0, { x: x, y: q0.y }, { x: x, y: q1.y }, q1];
        }
        if (side === 'left') {
            var xl = Math.min(q0.x, q1.x) - extra;
            return [q0, { x: xl, y: q0.y }, { x: xl, y: q1.y }, q1];
        }
        if (side === 'bottom') {
            var y = Math.max(q0.y, q1.y) + extra;
            return [q0, { x: q0.x, y: y }, { x: q1.x, y: y }, q1];
        }
        var yt = Math.min(q0.y, q1.y) - extra;
        return [q0, { x: q0.x, y: yt }, { x: q1.x, y: yt }, q1];
    }

    // Main routing entry point. Returns a list of waypoints from p0 to p1.
    function routeOrthogonal(p0, side0, p1, side1, obstacles) {
        var n0 = getSideNormal(side0), n1 = getSideNormal(side1);
        var q0 = { x: p0.x + n0.x * STUB_LENGTH, y: p0.y + n0.y * STUB_LENGTH };
        var q1 = { x: p1.x + n1.x * STUB_LENGTH, y: p1.y + n1.y * STUB_LENGTH };

        var sameAxis = (n0.x !== 0) === (n1.x !== 0);
        var opposite = (n0.x === -n1.x && n0.y === -n1.y);
        var same = (n0.x === n1.x && n0.y === n1.y);

        var route;
        if (opposite) {
            // Z-route on the axis the ports face along
            var axis = n0.x !== 0 ? 'x' : 'y';
            route = clearedZRoute(q0, q1, obstacles, axis) || (axis === 'x' ? zRouteX(q0, q1) : zRouteY(q0, q1));
        } else if (same) {
            route = uRoute(q0, q1, side0);
        } else {
            // Perpendicular ⇒ L-route
            route = lRoute(q0, q1, side0, side1);
        }

        // Prepend p0 and append p1 so the stub segments are explicit.
        return [p0].concat(route).concat([p1]);
    }

    // Convert a list of waypoints into an SVG path string. Each interior
    // corner is replaced by a quadratic curve of radius r, clamped to a third
    // of either adjacent segment so corners don't overshoot.
    function waypointsToPath(points, radius) {
        if (points.length < 2) return '';
        // De-dup adjacent identical waypoints, then collapse collinear
        // triples so degenerate "corners" from stub prepend/append don't
        // appear in the path string.
        var pts = [points[0]];
        for (var i = 1; i < points.length; i++) {
            var prev = pts[pts.length - 1];
            if (Math.abs(prev.x - points[i].x) < 0.5 && Math.abs(prev.y - points[i].y) < 0.5) continue;
            pts.push(points[i]);
        }
        for (var c = 1; c < pts.length - 1; ) {
            var a = pts[c - 1], b = pts[c], n = pts[c + 1];
            // Collinear if the cross product of (b-a) and (n-b) is ~0
            var cross = (b.x - a.x) * (n.y - b.y) - (b.y - a.y) * (n.x - b.x);
            if (Math.abs(cross) < 0.5) {
                pts.splice(c, 1);
            } else {
                c++;
            }
        }
        if (pts.length < 2) return '';
        if (pts.length === 2) return `M ${pts[0].x} ${pts[0].y} L ${pts[1].x} ${pts[1].y}`;

        var d = `M ${pts[0].x} ${pts[0].y}`;
        for (var k = 1; k < pts.length - 1; k++) {
            var a = pts[k - 1], b = pts[k], c = pts[k + 1];
            var ab = Math.hypot(b.x - a.x, b.y - a.y);
            var bc = Math.hypot(c.x - b.x, c.y - b.y);
            var r = Math.min(radius, ab / 2, bc / 2);
            // Point along a→b, r before b
            var t1 = ab > 0 ? (ab - r) / ab : 0;
            var p1x = a.x + (b.x - a.x) * t1;
            var p1y = a.y + (b.y - a.y) * t1;
            // Point along b→c, r after b
            var t2 = bc > 0 ? r / bc : 0;
            var p2x = b.x + (c.x - b.x) * t2;
            var p2y = b.y + (c.y - b.y) * t2;
            d += ` L ${p1x} ${p1y} Q ${b.x} ${b.y} ${p2x} ${p2y}`;
        }
        var last = pts[pts.length - 1];
        d += ` L ${last.x} ${last.y}`;
        return d;
    }

    function getPortId(port) {
        return port.dataset.groupId;
    }

    function getGroupEdgePoints(groupEl) {
        var x = parseFloat(groupEl.style.left);
        var y = parseFloat(groupEl.style.top);
        var rect = groupEl.getBoundingClientRect();
        var canvas = document.getElementById('builder-canvas');
        var zoom = canvas ? (zoomLevels[canvas.id] || 1) : 1;
        var w = rect.width / zoom;
        var h = rect.height / zoom;
        return { x: x, y: y, w: w, h: h, cx: x + w / 2, cy: y + h / 2 };
    }

    // Determine which side an edge connects on for a given group
    function determineSide(from, to) {
        var dx = to.cx - from.cx;
        var dy = to.cy - from.cy;
        if (Math.abs(dx) >= Math.abs(dy)) {
            return dx >= 0 ? 'right' : 'left';
        } else {
            return dy >= 0 ? 'bottom' : 'top';
        }
    }

    // Compute attachment point on a group side with offset for multiple edges
    function getSidePoint(group, side, index, total) {
        var fraction = (index + 1) / (total + 1);
        if (side === 'right') return { x: group.x + group.w, y: group.y + fraction * group.h };
        if (side === 'left') return { x: group.x, y: group.y + fraction * group.h };
        if (side === 'bottom') return { x: group.x + fraction * group.w, y: group.y + group.h };
        /* top */ return { x: group.x + fraction * group.w, y: group.y };
    }

    // Perpendicular pixel bias applied to one direction of a reverse-edge pair,
    // so A→B and B→A don't render on top of each other. Shifts the endpoint
    // along the group side (left/right sides → shift Y; top/bottom → shift X).
    var REVERSE_EDGE_BIAS = 8;

    function applyReverseBias(point, side, bias) {
        if (side === 'left' || side === 'right') return { x: point.x, y: point.y + bias };
        return { x: point.x + bias, y: point.y };
    }

    // Collect all edges and compute distributed endpoints
    function computeDistributedEdges(canvasSelector, edgeSelector, getGroupFn) {
        var canvas = document.querySelector(canvasSelector);
        if (!canvas) return [];

        var edges = Array.from(canvas.querySelectorAll(edgeSelector));
        if (!edges.length) return [];

        // Build edge data. Identify reverse-edge pairs in a single pass and tag
        // the second one with a perpendicular bias so it doesn't overlap.
        var edgeData = [];
        var seenPairs = {};
        edges.forEach(function(edge) {
            var fromGid = edge.dataset.fromGroup;
            var toGid = edge.dataset.toGroup;
            if (!fromGid || !toGid) return;

            var fromEl = canvas.querySelector('[data-group-id="' + fromGid + '"]');
            var toEl = canvas.querySelector('[data-group-id="' + toGid + '"]');
            if (!fromEl || !toEl) return;

            var from = getGroupFn(fromEl);
            var to = getGroupFn(toEl);
            var fromSide = determineSide(from, to);
            var toSide = determineSide(to, from);

            var forwardKey = fromGid + '->' + toGid;
            var reverseKey = toGid + '->' + fromGid;
            var bias = 0;
            if (seenPairs[reverseKey]) bias = REVERSE_EDGE_BIAS;
            seenPairs[forwardKey] = true;

            edgeData.push({ edge: edge, from: from, to: to, fromGid: fromGid, toGid: toGid, fromSide: fromSide, toSide: toSide, bias: bias });
        });

        // Bucket edges per (groupId, side), then sort each bucket by the spatial
        // position of the *other* group so an edge whose partner is above
        // attaches near the top of the side, etc. Insertion order would let
        // edges cross over each other on the same side.
        var sideMap = {};
        function pushSide(key, entry) {
            if (!sideMap[key]) sideMap[key] = [];
            sideMap[key].push(entry);
        }
        edgeData.forEach(function(d, i) {
            pushSide(d.fromGid + ':' + d.fromSide, { i: i, end: 'from', otherCenter: { x: d.to.cx, y: d.to.cy } });
            pushSide(d.toGid + ':' + d.toSide, { i: i, end: 'to', otherCenter: { x: d.from.cx, y: d.from.cy } });
        });

        function sortKey(side, c) {
            return (side === 'left' || side === 'right') ? c.y : c.x;
        }
        Object.keys(sideMap).forEach(function(key) {
            var side = key.split(':')[1];
            sideMap[key].sort(function(a, b) {
                return sortKey(side, a.otherCenter) - sortKey(side, b.otherCenter);
            });
        });

        // Compute endpoints with offsets using the sorted slot positions
        edgeData.forEach(function(d, i) {
            var fromList = sideMap[d.fromGid + ':' + d.fromSide];
            var toList = sideMap[d.toGid + ':' + d.toSide];
            var fromIdx = fromList.findIndex(function(e) { return e.i === i && e.end === 'from'; });
            var toIdx = toList.findIndex(function(e) { return e.i === i && e.end === 'to'; });

            var p1 = getSidePoint(d.from, d.fromSide, fromIdx, fromList.length);
            var p2 = getSidePoint(d.to, d.toSide, toIdx, toList.length);

            if (d.bias) {
                p1 = applyReverseBias(p1, d.fromSide, d.bias);
                p2 = applyReverseBias(p2, d.toSide, d.bias);
            }

            d.x1 = p1.x; d.y1 = p1.y;
            d.x2 = p2.x; d.y2 = p2.y;
        });

        return edgeData;
    }

    function setEdgePath(edgePath, d) {
        edgePath.setAttribute('d', d);
        var hit = edgePath.previousElementSibling;
        if (hit && hit.classList.contains('builder-edge-hit')) hit.setAttribute('d', d);
    }

    function updateGroupEdgePaths(groupId, newX, newY) {
        var groupEl = document.querySelector(`.builder-group[data-group-id="${groupId}"]`);
        if (!groupEl) return;

        // After moving a group, recalculate all builder edges with distribution
        recalculateBuilderEdges();
    }

    async function saveGroupPosition(templateId, groupId, x, y) {
        try {
            const token = getAntiforgeryToken();
            await fetch(`/api/templates/${templateId}/builder/groups/${groupId}/position`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ positionX: x, positionY: y })
            });
        } catch (err) {
            console.error('Failed to save group position:', err);
        }
    }

    function insertEdgeSVG(fromGroupId, toGroupId) {
        var fromEl = document.querySelector('.builder-group[data-group-id="' + fromGroupId + '"]');
        var toEl = document.querySelector('.builder-group[data-group-id="' + toGroupId + '"]');
        if (!fromEl || !toEl) return;

        var svg = document.querySelector('#builder-canvas svg');
        if (!svg) return;

        var from = getGroupEdgePoints(fromEl);
        var to = getGroupEdgePoints(toEl);

        // Simple center-to-center for the initial insert; recalculation will distribute
        var fromSide = determineSide(from, to);
        var toSide = determineSide(to, from);
        var p1 = getSidePoint(from, fromSide, 0, 1);
        var p2 = getSidePoint(to, toSide, 0, 1);
        var d = calculateBezierPath(p1.x, p1.y, p2.x, p2.y);

        var g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        g.setAttribute('class', 'builder-edge-group');

        var hit = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        hit.setAttribute('class', 'builder-edge-hit pointer-events-auto');
        hit.setAttribute('d', d);
        hit.dataset.fromGroup = fromGroupId;
        hit.dataset.toGroup = toGroupId;

        var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('class', 'builder-edge pointer-events-auto cursor-pointer');
        path.setAttribute('d', d);
        path.setAttribute('stroke', '#94a3b8');
        path.setAttribute('stroke-width', '2');
        path.setAttribute('fill', 'none');
        path.setAttribute('marker-end', 'url(#arrowhead)');
        path.dataset.fromGroup = fromGroupId;
        path.dataset.toGroup = toGroupId;

        g.appendChild(hit);
        g.appendChild(path);

        var tempLine = document.getElementById('temp-edge-line');
        if (tempLine) {
            svg.insertBefore(g, tempLine);
        } else {
            svg.appendChild(g);
        }

        // Recalculate all edges to distribute properly
        requestAnimationFrame(function() { recalculateBuilderEdges(); });
    }

    function removeEdgesByGroup(groupId) {
        document.querySelectorAll('.builder-edge-group').forEach(function (g) {
            var path = g.querySelector('.builder-edge');
            if (path && (path.dataset.fromGroup === groupId || path.dataset.toGroup === groupId)) {
                g.remove();
            }
        });
    }

    function createEdge(templateId, fromGroupId, toGroupId) {
        var fromGroup = document.querySelector('.builder-group[data-group-id="' + fromGroupId + '"]');
        var toGroup = document.querySelector('.builder-group[data-group-id="' + toGroupId + '"]');
        if (!fromGroup || !toGroup) return;

        var fromNode = fromGroup.querySelector('[data-node-id]');
        var toNode = toGroup.querySelector('[data-node-id]');
        if (!fromNode || !toNode) return;

        // Check for duplicate group-to-group edge in DOM
        var existing = document.querySelector('.builder-edge[data-from-group="' + fromGroupId + '"][data-to-group="' + toGroupId + '"]');
        if (existing) return;

        // Insert edge SVG immediately
        insertEdgeSVG(fromGroupId, toGroupId);

        // Persist to server
        var body = new FormData();
        body.append('TemplateId', templateId);
        body.append('FromGroupId', fromGroupId);
        body.append('FromNodeId', fromNode.dataset.nodeId);
        body.append('ToGroupId', toGroupId);
        body.append('ToNodeId', toNode.dataset.nodeId);

        fetch('/api/templates/' + templateId + '/builder/edges', {
            method: 'POST',
            headers: { 'RequestVerificationToken': getAntiforgeryToken() },
            body: body
        }).catch(function (err) {
            console.error('Failed to save edge:', err);
        });
    }

    // --- Multi-select helpers ---

    function updateSelectionVisuals() {
        document.querySelectorAll('.builder-group').forEach(function (el) {
            if (selectedGroupIds.has(el.dataset.groupId)) {
                el.classList.add('builder-group-selected');
            } else {
                el.classList.remove('builder-group-selected');
            }
        });
    }

    function clearSelection() {
        selectedGroupIds.clear();
        updateSelectionVisuals();
    }

    function toggleGroupSelection(groupId) {
        if (selectedGroupIds.has(groupId)) {
            selectedGroupIds.delete(groupId);
        } else {
            selectedGroupIds.add(groupId);
        }
        updateSelectionVisuals();
    }

    function selectAllGroups() {
        document.querySelectorAll('.builder-group').forEach(function (el) {
            selectedGroupIds.add(el.dataset.groupId);
        });
        updateSelectionVisuals();
    }

    // Ctrl+A to select all groups
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'a') {
            var canvas = document.getElementById('builder-canvas');
            if (!canvas) return;
            e.preventDefault();
            selectAllGroups();
        }
    });

    // --- Edge Connection (pointerdown on ANY port) ---

    document.addEventListener('pointerdown', function (e) {
        const port = e.target.closest('.builder-port');
        if (!port) return;

        e.preventDefault();
        e.stopPropagation();

        const canvas = document.getElementById('builder-canvas');
        if (!canvas) return;

        const innerEl = canvas.firstElementChild;
        const canvasRect = innerEl.getBoundingClientRect();
        const portRect = port.getBoundingClientRect();
        const zoom = zoomLevels[canvas.id] || 1;

        const startX = (portRect.left + portRect.width / 2 - canvasRect.left) / zoom;
        const startY = (portRect.top + portRect.height / 2 - canvasRect.top) / zoom;

        connectState = {
            fromGroupId: getPortId(port),
            templateId: canvas.dataset.templateId,
            startX: startX,
            startY: startY
        };

        const tempLine = document.getElementById('temp-edge-line');
        if (tempLine) {
            tempLine.setAttribute('x1', startX);
            tempLine.setAttribute('y1', startY);
            tempLine.setAttribute('x2', startX);
            tempLine.setAttribute('y2', startY);
            tempLine.classList.remove('hidden');
        }
    }, true);

    // --- Group Dragging (pointerdown) ---

    document.addEventListener('pointerdown', function (e) {
        if (e.target.closest('.builder-port')) return;
        if (e.target.closest('button')) return;
        if (e.target.closest('a')) return;
        if (e.target.closest('.builder-add-item-dropdown')) return;

        var groupEl = e.target.closest('.builder-group');
        if (!groupEl) return;

        var groupId = groupEl.dataset.groupId;

        // Ctrl+click toggles selection without starting drag
        if (e.ctrlKey || e.metaKey) {
            e.preventDefault();
            toggleGroupSelection(groupId);
            return;
        }

        e.preventDefault();
        groupEl.setPointerCapture(e.pointerId);

        const canvas = document.getElementById('builder-canvas');
        const innerEl = canvas.firstElementChild;
        const canvasRect = innerEl.getBoundingClientRect();
        const zoom = zoomLevels[canvas.id] || 1;

        // If this group is part of the selection, drag all selected groups
        // If not, clear selection and drag just this one
        if (!selectedGroupIds.has(groupId)) {
            clearSelection();
        }

        // Build list of groups to drag together
        var draggedGroups = [];
        var allDragIds = selectedGroupIds.size > 0 && selectedGroupIds.has(groupId)
            ? Array.from(selectedGroupIds) : [groupId];

        allDragIds.forEach(function (gid) {
            var el = document.querySelector('.builder-group[data-group-id="' + gid + '"]');
            if (el) {
                draggedGroups.push({
                    el: el,
                    id: gid,
                    startX: parseFloat(el.style.left) || 0,
                    startY: parseFloat(el.style.top) || 0
                });
            }
        });

        dragState = {
            el: groupEl,
            id: groupId,
            templateId: groupEl.dataset.templateId || canvas.dataset.templateId,
            startClientX: e.clientX,
            startClientY: e.clientY,
            canvasLeft: canvasRect.left,
            canvasTop: canvasRect.top,
            zoom: zoom,
            draggedGroups: draggedGroups,
            moved: false
        };
    });

    // --- Annotation Dragging (pointerdown) ---

    document.addEventListener('pointerdown', function (e) {
        if (e.target.closest('button')) return;
        var annotationEl = e.target.closest('.builder-annotation');
        if (!annotationEl) return;

        e.preventDefault();
        annotationEl.setPointerCapture(e.pointerId);

        var canvas = document.getElementById('builder-canvas');
        var zoom = canvas ? (zoomLevels[canvas.id] || 1) : 1;

        annotationDragState = {
            el: annotationEl,
            id: annotationEl.dataset.annotationId,
            templateId: annotationEl.dataset.templateId || (canvas && canvas.dataset.templateId),
            startX: parseFloat(annotationEl.style.left) || 0,
            startY: parseFloat(annotationEl.style.top) || 0,
            startClientX: e.clientX,
            startClientY: e.clientY,
            zoom: zoom,
            moved: false
        };
    });

    // --- Region Dragging (pointerdown, NOT on resize handles) ---

    document.addEventListener('pointerdown', function (e) {
        if (e.target.closest('.builder-resize-handle')) return;
        if (e.target.closest('button')) return;
        var regionEl = e.target.closest('.builder-region');
        if (!regionEl) return;

        e.preventDefault();
        regionEl.setPointerCapture(e.pointerId);

        var canvas = document.getElementById('builder-canvas');
        var zoom = canvas ? (zoomLevels[canvas.id] || 1) : 1;

        regionDragState = {
            el: regionEl,
            id: regionEl.dataset.regionId,
            templateId: regionEl.dataset.templateId || (canvas && canvas.dataset.templateId),
            startX: parseFloat(regionEl.style.left) || 0,
            startY: parseFloat(regionEl.style.top) || 0,
            startClientX: e.clientX,
            startClientY: e.clientY,
            zoom: zoom,
            moved: false
        };
    });

    // --- Region Resizing (pointerdown on resize handle) ---

    document.addEventListener('pointerdown', function (e) {
        var handle = e.target.closest('.builder-resize-handle');
        if (!handle) return;

        e.preventDefault();
        e.stopPropagation();

        var regionEl = handle.closest('.builder-region');
        if (!regionEl) return;

        handle.setPointerCapture(e.pointerId);

        var canvas = document.getElementById('builder-canvas');
        var zoom = canvas ? (zoomLevels[canvas.id] || 1) : 1;

        resizeState = {
            el: regionEl,
            id: regionEl.dataset.regionId,
            templateId: regionEl.dataset.templateId || (canvas && canvas.dataset.templateId),
            corner: handle.dataset.corner,
            startClientX: e.clientX,
            startClientY: e.clientY,
            startX: parseFloat(regionEl.style.left) || 0,
            startY: parseFloat(regionEl.style.top) || 0,
            startW: parseFloat(regionEl.style.width) || 300,
            startH: parseFloat(regionEl.style.height) || 200,
            zoom: zoom
        };
    }, true);

    // --- Canvas Panning (pointerdown on empty canvas) ---

    document.addEventListener('pointerdown', function (e) {
        if (dragState || connectState || annotationDragState || regionDragState || resizeState) return;
        var canvas = e.target.closest('#builder-canvas') || e.target.closest('#viewer-canvas');
        if (!canvas) return;
        if (e.target.closest('.builder-group') || e.target.closest('.builder-annotation') || e.target.closest('.builder-region') || e.target.closest('.builder-edge') || e.target.closest('.builder-edge-hit') || e.target.closest('.viewer-group') || e.target.closest('.viewer-node')) return;

        // Clicking on empty canvas clears selection
        if (canvas.id === 'builder-canvas' && !(e.ctrlKey || e.metaKey)) {
            clearSelection();
        }

        panState = {
            startX: e.clientX,
            startY: e.clientY,
            scrollLeft: canvas.scrollLeft,
            scrollTop: canvas.scrollTop,
            canvas: canvas
        };
        canvas.style.cursor = 'grabbing';
    });

    // --- Pointer Move ---

    document.addEventListener('pointermove', function (e) {
        if (dragState) {
            e.preventDefault();
            var zoom = dragState.zoom || 1;
            var dx = (e.clientX - dragState.startClientX) / zoom;
            var dy = (e.clientY - dragState.startClientY) / zoom;
            dragState.moved = true;

            dragState.draggedGroups.forEach(function (g) {
                var newX = Math.max(0, g.startX + dx);
                var newY = Math.max(0, g.startY + dy);
                g.el.style.left = newX + 'px';
                g.el.style.top = newY + 'px';
            });

            // Recalculate all edges with distribution during drag
            recalculateBuilderEdges();

            // Expand canvas if group is dragged near the edge
            var canvas = document.getElementById('builder-canvas');
            if (canvas) {
                var innerEl = canvas.firstElementChild;
                var padding = 300;
                dragState.draggedGroups.forEach(function (g) {
                    var gx = parseFloat(g.el.style.left);
                    var gy = parseFloat(g.el.style.top);
                    var groupRect = g.el.getBoundingClientRect();
                    var neededW = gx + groupRect.width / zoom + padding;
                    var neededH = gy + groupRect.height / zoom + padding;
                    if (neededW > parseFloat(innerEl.style.minWidth))
                        innerEl.style.minWidth = neededW + 'px';
                    if (neededH > parseFloat(innerEl.style.minHeight))
                        innerEl.style.minHeight = neededH + 'px';
                });
                var svg = innerEl.querySelector('svg');
                if (svg) {
                    svg.style.minWidth = innerEl.style.minWidth;
                    svg.style.minHeight = innerEl.style.minHeight;
                }
            }
        }

        if (annotationDragState) {
            e.preventDefault();
            var zoom = annotationDragState.zoom || 1;
            var dx = (e.clientX - annotationDragState.startClientX) / zoom;
            var dy = (e.clientY - annotationDragState.startClientY) / zoom;
            annotationDragState.moved = true;
            annotationDragState.el.style.left = Math.max(0, annotationDragState.startX + dx) + 'px';
            annotationDragState.el.style.top = Math.max(0, annotationDragState.startY + dy) + 'px';
            expandCanvasForElement(annotationDragState.el, zoom);
        }

        if (regionDragState) {
            e.preventDefault();
            var zoom = regionDragState.zoom || 1;
            var dx = (e.clientX - regionDragState.startClientX) / zoom;
            var dy = (e.clientY - regionDragState.startClientY) / zoom;
            regionDragState.moved = true;
            regionDragState.el.style.left = Math.max(0, regionDragState.startX + dx) + 'px';
            regionDragState.el.style.top = Math.max(0, regionDragState.startY + dy) + 'px';
            expandCanvasForElement(regionDragState.el, zoom);
        }

        if (resizeState) {
            e.preventDefault();
            var zoom = resizeState.zoom || 1;
            var dx = (e.clientX - resizeState.startClientX) / zoom;
            var dy = (e.clientY - resizeState.startClientY) / zoom;
            var corner = resizeState.corner;
            var newW = resizeState.startW, newH = resizeState.startH;
            var newX = resizeState.startX, newY = resizeState.startY;

            if (corner === 'se') {
                newW = Math.max(50, resizeState.startW + dx);
                newH = Math.max(50, resizeState.startH + dy);
            } else if (corner === 'sw') {
                newW = Math.max(50, resizeState.startW - dx);
                newH = Math.max(50, resizeState.startH + dy);
                newX = resizeState.startX + (resizeState.startW - newW);
            } else if (corner === 'ne') {
                newW = Math.max(50, resizeState.startW + dx);
                newH = Math.max(50, resizeState.startH - dy);
                newY = resizeState.startY + (resizeState.startH - newH);
            } else if (corner === 'nw') {
                newW = Math.max(50, resizeState.startW - dx);
                newH = Math.max(50, resizeState.startH - dy);
                newX = resizeState.startX + (resizeState.startW - newW);
                newY = resizeState.startY + (resizeState.startH - newH);
            }

            resizeState.el.style.left = Math.max(0, newX) + 'px';
            resizeState.el.style.top = Math.max(0, newY) + 'px';
            resizeState.el.style.width = newW + 'px';
            resizeState.el.style.height = newH + 'px';
            expandCanvasForElement(resizeState.el, zoom);
        }

        if (connectState) {
            e.preventDefault();
            var canvas = document.getElementById('builder-canvas');
            if (!canvas) return;
            var innerEl = canvas.firstElementChild;
            var canvasRect = innerEl.getBoundingClientRect();
            var zoom = zoomLevels[canvas.id] || 1;
            var tempLine = document.getElementById('temp-edge-line');
            if (tempLine) {
                tempLine.setAttribute('x2', (e.clientX - canvasRect.left) / zoom);
                tempLine.setAttribute('y2', (e.clientY - canvasRect.top) / zoom);
            }
        }

        if (panState) {
            e.preventDefault();
            var dx = e.clientX - panState.startX;
            var dy = e.clientY - panState.startY;

            panState.canvas.scrollLeft = panState.scrollLeft - dx;
            panState.canvas.scrollTop = panState.scrollTop - dy;
        }
    });

    // --- Pointer Up ---

    document.addEventListener('pointerup', function (e) {
        if (dragState) {
            if (dragState.moved) {
                dragState.draggedGroups.forEach(function (g) {
                    var x = parseFloat(g.el.style.left);
                    var y = parseFloat(g.el.style.top);
                    saveGroupPosition(dragState.templateId, g.id, x, y);
                });
            }
            dragState = null;
        }

        if (annotationDragState) {
            if (annotationDragState.moved) {
                var x = parseFloat(annotationDragState.el.style.left);
                var y = parseFloat(annotationDragState.el.style.top);
                var token = getAntiforgeryToken();
                fetch('/api/templates/' + annotationDragState.templateId + '/builder/annotations/' + annotationDragState.id + '/position', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify({ positionX: x, positionY: y })
                }).catch(function (err) { console.error('Failed to save annotation position:', err); });
            }
            annotationDragState = null;
        }

        if (regionDragState) {
            if (regionDragState.moved) {
                var x = parseFloat(regionDragState.el.style.left);
                var y = parseFloat(regionDragState.el.style.top);
                var token = getAntiforgeryToken();
                fetch('/api/templates/' + regionDragState.templateId + '/builder/regions/' + regionDragState.id + '/position', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify({ positionX: x, positionY: y })
                }).catch(function (err) { console.error('Failed to save region position:', err); });
            }
            regionDragState = null;
        }

        if (resizeState) {
            var w = parseFloat(resizeState.el.style.width);
            var h = parseFloat(resizeState.el.style.height);
            var x = parseFloat(resizeState.el.style.left);
            var y = parseFloat(resizeState.el.style.top);
            var token = getAntiforgeryToken();
            // Save both size and position (position may change for nw/ne/sw corners)
            fetch('/api/templates/' + resizeState.templateId + '/builder/regions/' + resizeState.id + '/size', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ width: w, height: h })
            }).catch(function (err) { console.error('Failed to save region size:', err); });
            // Also save position if it changed
            if (resizeState.corner !== 'se') {
                fetch('/api/templates/' + resizeState.templateId + '/builder/regions/' + resizeState.id + '/position', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify({ positionX: x, positionY: y })
                }).catch(function (err) { console.error('Failed to save region position:', err); });
            }
            resizeState = null;
        }

        if (connectState) {
            var targetPort = e.target.closest('.builder-port');
            if (targetPort) {
                var toGroupId = getPortId(targetPort);
                if (toGroupId !== connectState.fromGroupId) {
                    createEdge(connectState.templateId, connectState.fromGroupId, toGroupId);
                }
            }
            var tempLine = document.getElementById('temp-edge-line');
            if (tempLine) tempLine.classList.add('hidden');
            connectState = null;
        }

        if (panState) {
            panState.canvas.style.cursor = '';
            panState = null;
        }
    });

    // --- Add Item Dropdown Toggle ---

    document.addEventListener('click', function (e) {
        var toggle = e.target.closest('.builder-add-item-toggle');
        if (toggle) {
            e.preventDefault();
            e.stopPropagation();
            var dropdown = toggle.nextElementSibling;
            if (dropdown) dropdown.classList.toggle('hidden');
            return;
        }

        // Close all open dropdowns when clicking elsewhere
        document.querySelectorAll('.builder-add-item-dropdown').forEach(function (dd) {
            if (!dd.contains(e.target)) dd.classList.add('hidden');
        });
    });

    // --- Viewer: Update edge colors after completion toggle ---

    function isGroupCompleted(groupEl) {
        var items = groupEl.querySelectorAll('[data-completed]');
        if (items.length === 0) return false;
        for (var i = 0; i < items.length; i++) {
            if (items[i].dataset.completed !== 'true') return false;
        }
        return true;
    }

    function updateViewerEdgeColors() {
        document.querySelectorAll('.viewer-edge').forEach(function (edge) {
            var fromGid = edge.dataset.fromGroup;
            var toGid = edge.dataset.toGroup;
            if (!fromGid || !toGid) return;

            var fromGroup = document.querySelector(`.viewer-group[data-group-id="${fromGid}"]`);
            var toGroup = document.querySelector(`.viewer-group[data-group-id="${toGid}"]`);
            if (!fromGroup || !toGroup) return;

            var completed = isGroupCompleted(fromGroup) && isGroupCompleted(toGroup);
            edge.setAttribute('stroke', completed ? '#22c55e' : '#94a3b8');
            edge.setAttribute('marker-end', completed ? 'url(#viewer-arrowhead-completed)' : 'url(#viewer-arrowhead)');
        });
    }

    // --- Edge Recalculation (DOM-measured, with distribution) ---

    function getGroupRectFromDOM(canvasInnerEl, groupEl, zoom) {
        var z = zoom || 1;
        var canvasRect = canvasInnerEl.getBoundingClientRect();
        var groupRect = groupEl.getBoundingClientRect();
        // getBoundingClientRect returns post-transform (scaled) viewport
        // coordinates; SVG paths are in unscaled canvas coordinates, so divide.
        var x = (groupRect.left - canvasRect.left) / z;
        var y = (groupRect.top - canvasRect.top) / z;
        var w = groupRect.width / z;
        var h = groupRect.height / z;
        return { x: x, y: y, w: w, h: h, cx: x + w / 2, cy: y + h / 2 };
    }

    // Collect all rendered group rectangles for obstacle avoidance. Includes a
    // `gid` field so the router can exclude an edge's own source/destination.
    function collectAllGroupRects(canvas, innerEl, zoom) {
        return Array.from(canvas.querySelectorAll('.viewer-group, .builder-group')).map(function(el) {
            var r = getGroupRectFromDOM(innerEl, el, zoom);
            r.gid = el.dataset.groupId;
            return r;
        });
    }

    function renderEdge(d, allRects, applyFn) {
        var obstacles = buildObstacleList(allRects, d.fromGid, d.toGid);
        var waypoints = routeOrthogonal(
            { x: d.x1, y: d.y1 }, d.fromSide,
            { x: d.x2, y: d.y2 }, d.toSide,
            obstacles
        );
        applyFn(waypointsToPath(waypoints, CORNER_RADIUS));
    }

    function recalculateViewerEdges() {
        var canvas = document.getElementById('viewer-canvas');
        if (!canvas) return;
        var innerEl = canvas.firstElementChild;
        if (!innerEl) return;
        var zoom = zoomLevels[canvas.id] || 1;

        var edgeData = computeDistributedEdges('#viewer-canvas', '.viewer-edge', function(el) {
            return getGroupRectFromDOM(innerEl, el, zoom);
        });
        var allRects = collectAllGroupRects(canvas, innerEl, zoom);

        edgeData.forEach(function(d) {
            renderEdge(d, allRects, function(path) { d.edge.setAttribute('d', path); });
        });
    }

    function recalculateBuilderEdges() {
        var canvas = document.getElementById('builder-canvas');
        if (!canvas) return;
        var innerEl = canvas.firstElementChild;
        if (!innerEl) return;
        var zoom = zoomLevels[canvas.id] || 1;

        var edgeData = computeDistributedEdges('#builder-canvas', '.builder-edge', function(el) {
            return getGroupRectFromDOM(innerEl, el, zoom);
        });
        var allRects = collectAllGroupRects(canvas, innerEl, zoom);

        edgeData.forEach(function(d) {
            renderEdge(d, allRects, function(path) { setEdgePath(d.edge, path); });
        });
    }

    window.KlavLorEdges = {
        recalculateBuilder: recalculateBuilderEdges,
        recalculateViewer: recalculateViewerEdges
    };

    document.addEventListener('htmx:afterSettle', function (e) {
        var targetId = e.detail.target && e.detail.target.id;
        if (!targetId) return;

        if (targetId === 'hx-page-container' || targetId === 'viewer-canvas' || targetId === 'builder-canvas') {
            // Reset zoom to 1 on page navigation — stale zoom from a previous
            // template would distort edge calculations on the new one.
            zoomLevels['builder-canvas'] = 1;
            zoomLevels['viewer-canvas'] = 1;
            ['builder-canvas', 'viewer-canvas'].forEach(function (id) {
                var canvas = document.getElementById(id);
                if (canvas) applyZoom(canvas, 1);
            });

            // Double-rAF: first rAF queues after the browser commits layout for the
            // new HTMX content; the second fires after that frame is painted, ensuring
            // getBoundingClientRect() returns final dimensions.
            requestAnimationFrame(function () {
                requestAnimationFrame(function () {
                    recalculateViewerEdges();
                    recalculateBuilderEdges();
                    centerCanvasOnNodes('builder-canvas');
                    centerCanvasOnNodes('viewer-canvas');
                });
            });
        }

        // After a targeted group swap (add item, edit node), recalculate edges
        if (targetId.startsWith('builder-group-') || targetId === 'builder-canvas-inner') {
            requestAnimationFrame(function () {
                requestAnimationFrame(function () {
                    recalculateBuilderEdges();
                });
            });
        }

        if (targetId.startsWith('viewer-node-')) {
            updateViewerEdgeColors();
        }
    });

    // --- Confirmation Modal ---

    function showConfirmModal(message, onConfirm) {
        var existing = document.getElementById('builder-confirm-modal');
        if (existing) existing.remove();

        var modal = document.createElement('div');
        modal.id = 'builder-confirm-modal';
        modal.className = 'fixed inset-0 z-50 flex items-center justify-center p-4';
        modal.innerHTML =
            '<div class="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" data-dismiss></div>' +
            '<div class="relative bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-700 shadow-xl max-w-sm w-full overflow-hidden">' +
                '<div class="px-6 py-5">' +
                    '<h3 class="text-lg font-semibold text-slate-900 dark:text-slate-100">Confirm</h3>' +
                    '<p class="mt-2 text-sm text-slate-600 dark:text-slate-400" id="confirm-message"></p>' +
                '</div>' +
                '<div class="px-6 py-3 bg-slate-50 dark:bg-slate-800 border-t border-slate-200 dark:border-slate-700 flex items-center justify-end gap-3">' +
                    '<button type="button" data-dismiss class="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-600 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 cursor-pointer">Cancel</button>' +
                    '<button type="button" data-confirm class="px-4 py-2 text-sm font-semibold text-white bg-red-500 rounded-lg hover:bg-red-600 cursor-pointer">Delete</button>' +
                '</div>' +
            '</div>';
        modal.querySelector('#confirm-message').textContent = message;

        modal.addEventListener('click', function (e) {
            if (e.target.closest('[data-dismiss]')) {
                modal.remove();
            }
            if (e.target.closest('[data-confirm]')) {
                modal.remove();
                onConfirm();
            }
        });

        document.body.appendChild(modal);
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.builder-confirm-delete');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();

        var url = btn.dataset.deleteUrl;
        var message = btn.dataset.deleteMessage || 'Are you sure?';
        var groupEl = btn.closest('.builder-group');
        var groupId = groupEl ? groupEl.dataset.groupId : null;

        showConfirmModal(message, function () {
            fetch(url, {
                method: 'DELETE',
                headers: { 'RequestVerificationToken': getAntiforgeryToken() }
            }).then(function (res) {
                if (!res.ok) return;
                // Remove edges connected to this group
                if (groupId) removeEdgesByGroup(groupId);
                // Remove the group element
                if (groupEl) groupEl.remove();
            }).catch(function (err) {
                console.error('Failed to delete:', err);
            });
        });
    });

    // --- Node Deletion (fetch + targeted group refresh) ---

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.builder-delete-node');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();

        var templateId = btn.dataset.templateId;
        var nodeId = btn.dataset.nodeId;
        var groupId = btn.dataset.groupId;

        fetch('/api/templates/' + templateId + '/builder/nodes/' + nodeId, {
            method: 'DELETE',
            headers: { 'RequestVerificationToken': getAntiforgeryToken() }
        }).then(function (res) {
            if (!res.ok) return;
            return res.json();
        }).then(function (data) {
            if (!data) return;
            // Remove SVG edges for group connections that no longer exist
            if (data.removedGroupPairs && data.removedGroupPairs.length > 0) {
                data.removedGroupPairs.forEach(function (pair) {
                    var fromGid = String(pair[0]);
                    var toGid = String(pair[1]);
                    document.querySelectorAll('.builder-edge-group').forEach(function (g) {
                        var path = g.querySelector('.builder-edge');
                        if (path && path.dataset.fromGroup === fromGid && path.dataset.toGroup === toGid) {
                            g.remove();
                        }
                    });
                });
            }
            // Refresh the group to show updated node list
            if (data.groupStillExists && groupId) {
                htmx.ajax('GET', '/api/templates/' + templateId + '/builder/groups/' + groupId, {
                    target: '#builder-group-' + groupId,
                    swap: 'outerHTML'
                });
                // afterSettle will trigger recalculateBuilderEdges
            } else if (groupId) {
                // Group was removed — remove it from DOM
                var groupEl = document.getElementById('builder-group-' + groupId);
                if (groupEl) groupEl.remove();
                removeEdgesByGroup(groupId);
            }
        }).catch(function (err) {
            console.error('Failed to delete node:', err);
        });
    });

    // --- Edge Deletion (no confirm, just delete on click) ---

    document.addEventListener('click', function (e) {
        var edgeHit = e.target.closest('.builder-edge-hit');
        var edgePath = edgeHit || e.target.closest('.builder-edge');
        if (!edgePath) return;

        var canvas = document.getElementById('builder-canvas');
        if (!canvas) return;
        var templateId = canvas.dataset.templateId;
        var edgeId = edgePath.dataset.edgeId;
        var edgeGroup = edgePath.closest('.builder-edge-group');

        // Remove from DOM immediately
        if (edgeGroup) edgeGroup.remove();

        // Recalculate remaining edges to redistribute
        requestAnimationFrame(function() { recalculateBuilderEdges(); });

        // Persist to server
        if (edgeId) {
            fetch('/api/templates/' + templateId + '/builder/edges/' + edgeId, {
                method: 'DELETE',
                headers: { 'RequestVerificationToken': getAntiforgeryToken() }
            }).catch(function (err) {
                console.error('Failed to delete edge:', err);
            });
        }
    });

    // --- Annotation Deletion ---

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.builder-annotation-delete');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();

        var templateId = btn.dataset.templateId;
        var annotationId = btn.dataset.annotationId;
        var annotationEl = document.getElementById('builder-annotation-' + annotationId);

        showConfirmModal('Delete this annotation?', function () {
            fetch('/api/templates/' + templateId + '/builder/annotations/' + annotationId, {
                method: 'DELETE',
                headers: { 'RequestVerificationToken': getAntiforgeryToken() }
            }).then(function (res) {
                if (res.ok && annotationEl) annotationEl.remove();
            }).catch(function (err) {
                console.error('Failed to delete annotation:', err);
            });
        });
    });

    // --- Region Deletion ---

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.builder-region-delete');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();

        var templateId = btn.dataset.templateId;
        var regionId = btn.dataset.regionId;
        var regionEl = document.getElementById('builder-region-' + regionId);

        showConfirmModal('Delete this region?', function () {
            fetch('/api/templates/' + templateId + '/builder/regions/' + regionId, {
                method: 'DELETE',
                headers: { 'RequestVerificationToken': getAntiforgeryToken() }
            }).then(function (res) {
                if (res.ok && regionEl) regionEl.remove();
            }).catch(function (err) {
                console.error('Failed to delete region:', err);
            });
        });
    });

    // --- Canvas Zoom & Scroll ---

    var zoomLevels = { 'builder-canvas': 1, 'viewer-canvas': 1 };
    var MIN_ZOOM = 0.25;
    var MAX_ZOOM = 2;

    function applyZoom(canvas, zoom) {
        var innerEl = canvas.firstElementChild;
        if (!innerEl) return;
        innerEl.style.transform = 'scale(' + zoom + ')';
        innerEl.style.transformOrigin = '0 0';
    }

    document.addEventListener('wheel', function (e) {
        var canvas = e.target.closest('#builder-canvas') || e.target.closest('#viewer-canvas');
        if (!canvas) return;
        e.preventDefault();

        var canvasId = canvas.id;

        if (e.ctrlKey || e.metaKey) {
            // Zoom
            var oldZoom = zoomLevels[canvasId] || 1;
            var delta = e.deltaY > 0 ? -0.1 : 0.1;
            var newZoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, oldZoom + delta));
            if (newZoom === oldZoom) return;

            // Zoom toward cursor position
            var rect = canvas.getBoundingClientRect();
            var cursorX = e.clientX - rect.left + canvas.scrollLeft;
            var cursorY = e.clientY - rect.top + canvas.scrollTop;

            zoomLevels[canvasId] = newZoom;
            applyZoom(canvas, newZoom);

            // Adjust scroll to keep cursor position stable
            var ratio = newZoom / oldZoom;
            canvas.scrollLeft = cursorX * ratio - (e.clientX - rect.left);
            canvas.scrollTop = cursorY * ratio - (e.clientY - rect.top);
        } else {
            // Pan
            canvas.scrollLeft += e.deltaX || 0;
            canvas.scrollTop += e.deltaY || 0;
        }
    }, { passive: false });

    // --- Auto-Center Canvas on Nodes ---

    function centerCanvasOnNodes(canvasId) {
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;
        var groups = canvas.querySelectorAll('.builder-group, .viewer-group');
        if (!groups.length) return;

        var zoom = zoomLevels[canvasId] || 1;
        var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        groups.forEach(function(g) {
            var x = parseFloat(g.style.left);
            var y = parseFloat(g.style.top);
            var rect = g.getBoundingClientRect();
            var w = rect.width / zoom;
            var h = rect.height / zoom;
            minX = Math.min(minX, x);
            minY = Math.min(minY, y);
            maxX = Math.max(maxX, x + w);
            maxY = Math.max(maxY, y + h);
        });

        var contentCenterX = (minX + maxX) / 2;
        var contentCenterY = (minY + maxY) / 2;

        canvas.scrollLeft = contentCenterX * zoom - canvas.clientWidth / 2;
        canvas.scrollTop = contentCenterY * zoom - canvas.clientHeight / 2;
    }

    // --- Node Search ---

    var searchDebounceTimer = null;

    function searchAndHighlightNodes(query) {
        // Clear previous highlights
        document.querySelectorAll('.search-highlight').forEach(function(el) {
            el.classList.remove('search-highlight');
        });

        if (!query || !query.trim()) return;

        var lowerQuery = query.toLowerCase().trim();
        var canvas = document.getElementById('builder-canvas') || document.getElementById('viewer-canvas');
        if (!canvas) return;

        var canvasId = canvas.id;
        var groups = canvas.querySelectorAll('.builder-group, .viewer-group');
        var firstMatch = null;

        groups.forEach(function(group) {
            var labels = group.querySelectorAll('.text-xs, .text-\\[11px\\]');
            var found = false;
            labels.forEach(function(label) {
                if (label.textContent.toLowerCase().includes(lowerQuery)) {
                    found = true;
                }
            });
            if (found) {
                group.classList.add('search-highlight');
                if (!firstMatch) firstMatch = group;
            }
        });

        // Scroll to first match
        if (firstMatch && canvas) {
            var zoom = zoomLevels[canvasId] || 1;
            var x = parseFloat(firstMatch.style.left) || 0;
            var y = parseFloat(firstMatch.style.top) || 0;
            var rect = firstMatch.getBoundingClientRect();
            var w = rect.width / zoom;
            var h = rect.height / zoom;
            canvas.scrollLeft = (x + w / 2) * zoom - canvas.clientWidth / 2;
            canvas.scrollTop = (y + h / 2) * zoom - canvas.clientHeight / 2;
        }
    }

    document.addEventListener('input', function(e) {
        if (e.target.id !== 'canvas-search-input') return;
        clearTimeout(searchDebounceTimer);
        searchDebounceTimer = setTimeout(function() {
            searchAndHighlightNodes(e.target.value);
        }, 250);
    });

    document.addEventListener('keydown', function(e) {
        // Escape clears search
        if (e.key === 'Escape') {
            var input = document.getElementById('canvas-search-input');
            if (input && (document.activeElement === input || input.value)) {
                input.value = '';
                searchAndHighlightNodes('');
                input.blur();
                e.preventDefault();
                return;
            }
        }
        // Ctrl+F / Cmd+F focuses search
        if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
            var canvas = document.getElementById('builder-canvas') || document.getElementById('viewer-canvas');
            if (canvas) {
                var input = document.getElementById('canvas-search-input');
                if (input) {
                    e.preventDefault();
                    input.focus();
                    input.select();
                }
            }
        }
    });

    // --- Initial edge recalculation and centering on page load ---

    document.addEventListener('DOMContentLoaded', function () {
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                recalculateViewerEdges();
                recalculateBuilderEdges();
                centerCanvasOnNodes('builder-canvas');
                centerCanvasOnNodes('viewer-canvas');
            });
        });
    });

    // Recompute on viewport resize. Debounced because group bounding boxes
    // can shift if a label wraps differently at the new width.
    var resizeDebounce = null;
    window.addEventListener('resize', function () {
        clearTimeout(resizeDebounce);
        resizeDebounce = setTimeout(function () {
            recalculateViewerEdges();
            recalculateBuilderEdges();
        }, 150);
    });
})();
