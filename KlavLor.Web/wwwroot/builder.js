// builder.js - DAG Builder Canvas Interactions

(function () {
    let dragState = null;
    let connectState = null;
    let panState = null;

    // --- Helpers ---

    function getAntiforgeryToken() {
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function calculateBezierPath(x1, y1, x2, y2) {
        var dx = Math.abs(x2 - x1);
        var dy = Math.abs(y2 - y1);

        if (dx >= dy) {
            var cpOffset = Math.max(dx * 0.4, 50);
            return `M ${x1} ${y1} C ${x1 + cpOffset} ${y1}, ${x2 - cpOffset} ${y2}, ${x2} ${y2}`;
        } else {
            var cpOffset = Math.max(dy * 0.4, 50);
            var cpDir = y2 > y1 ? 1 : -1;
            return `M ${x1} ${y1} C ${x1} ${y1 + cpOffset * cpDir}, ${x2} ${y2 - cpOffset * cpDir}, ${x2} ${y2}`;
        }
    }

    function getPortId(port) {
        return port.dataset.groupId;
    }

    function getGroupEdgePoints(groupEl) {
        var x = parseFloat(groupEl.style.left);
        var y = parseFloat(groupEl.style.top);
        var rect = groupEl.getBoundingClientRect();
        var w = rect.width;
        var h = rect.height;
        return { x: x, y: y, w: w, h: h, cx: x + w / 2, cy: y + h / 2 };
    }

    function computeEdgeEndpoints(from, to) {
        var x1, y1, x2, y2;
        var dx = to.cx - from.cx;
        var dy = to.cy - from.cy;

        if (Math.abs(dx) >= Math.abs(dy)) {
            if (dx >= 0) {
                x1 = from.x + from.w; y1 = from.y + from.h / 2;
                x2 = to.x; y2 = to.y + to.h / 2;
            } else {
                x1 = from.x; y1 = from.y + from.h / 2;
                x2 = to.x + to.w; y2 = to.y + to.h / 2;
            }
        } else {
            if (dy >= 0) {
                x1 = from.cx; y1 = from.y + from.h;
                x2 = to.cx; y2 = to.y;
            } else {
                x1 = from.cx; y1 = from.y;
                x2 = to.cx; y2 = to.y + to.h;
            }
        }
        return { x1, y1, x2, y2 };
    }

    function setEdgePath(edgePath, d) {
        edgePath.setAttribute('d', d);
        var hit = edgePath.previousElementSibling;
        if (hit && hit.classList.contains('builder-edge-hit')) hit.setAttribute('d', d);
    }

    function updateGroupEdgePaths(groupId, newX, newY) {
        var groupEl = document.querySelector(`.builder-group[data-group-id="${groupId}"]`);
        if (!groupEl) return;
        var rect = groupEl.getBoundingClientRect();
        var gw = rect.width;
        var gh = rect.height;
        var fromPt = { x: newX, y: newY, w: gw, h: gh, cx: newX + gw / 2, cy: newY + gh / 2 };

        document.querySelectorAll(`.builder-edge[data-from-group="${groupId}"]`).forEach(function (path) {
            var toGroupId = path.dataset.toGroup;
            if (!toGroupId) return;
            var toGroupEl = document.querySelector(`.builder-group[data-group-id="${toGroupId}"]`);
            if (!toGroupEl) return;
            var toPt = getGroupEdgePoints(toGroupEl);
            var ep = computeEdgeEndpoints(fromPt, toPt);
            setEdgePath(path, calculateBezierPath(ep.x1, ep.y1, ep.x2, ep.y2));
        });

        document.querySelectorAll(`.builder-edge[data-to-group="${groupId}"]`).forEach(function (path) {
            var fromGroupId = path.dataset.fromGroup;
            if (!fromGroupId) return;
            var fromGroupEl = document.querySelector(`.builder-group[data-group-id="${fromGroupId}"]`);
            if (!fromGroupEl) return;
            var srcPt = getGroupEdgePoints(fromGroupEl);
            var ep = computeEdgeEndpoints(srcPt, fromPt);
            setEdgePath(path, calculateBezierPath(ep.x1, ep.y1, ep.x2, ep.y2));
        });
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

    function createEdge(templateId, fromGroupId, toGroupId) {
        // Pick any node from each group to create the edge
        var fromGroup = document.querySelector(`.builder-group[data-group-id="${fromGroupId}"]`);
        var toGroup = document.querySelector(`.builder-group[data-group-id="${toGroupId}"]`);
        if (!fromGroup || !toGroup) return;

        var fromNode = fromGroup.querySelector('[data-node-id]');
        var toNode = toGroup.querySelector('[data-node-id]');
        if (!fromNode || !toNode) return;

        var values = {
            TemplateId: templateId,
            FromGroupId: fromGroupId,
            FromNodeId: fromNode.dataset.nodeId,
            ToGroupId: toGroupId,
            ToNodeId: toNode.dataset.nodeId
        };

        htmx.ajax('POST', `/api/templates/${templateId}/builder/edges`, {
            target: '#builder-canvas',
            swap: 'innerHTML',
            values: values
        });
    }

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

        const startX = portRect.left + portRect.width / 2 - canvasRect.left;
        const startY = portRect.top + portRect.height / 2 - canvasRect.top;

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

        e.preventDefault();
        groupEl.setPointerCapture(e.pointerId);

        const rect = groupEl.getBoundingClientRect();
        const canvas = document.getElementById('builder-canvas');
        const innerEl = canvas.firstElementChild;
        const canvasRect = innerEl.getBoundingClientRect();

        dragState = {
            el: groupEl,
            id: groupEl.dataset.groupId,
            templateId: groupEl.dataset.templateId || canvas.dataset.templateId,
            offsetX: e.clientX - rect.left,
            offsetY: e.clientY - rect.top,
            canvasLeft: canvasRect.left,
            canvasTop: canvasRect.top,
            moved: false
        };
    });

    // --- Canvas Panning (pointerdown on empty canvas) ---

    document.addEventListener('pointerdown', function (e) {
        if (dragState || connectState) return;
        var canvas = e.target.closest('#builder-canvas') || e.target.closest('#viewer-canvas');
        if (!canvas) return;
        if (e.target.closest('.builder-group') || e.target.closest('.builder-edge') || e.target.closest('.builder-edge-hit') || e.target.closest('.viewer-group') || e.target.closest('.viewer-node')) return;

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
            var x = Math.max(0, e.clientX - dragState.canvasLeft - dragState.offsetX);
            var y = Math.max(0, e.clientY - dragState.canvasTop - dragState.offsetY);

            dragState.el.style.left = x + 'px';
            dragState.el.style.top = y + 'px';
            dragState.moved = true;

            // Expand canvas if group is dragged near the edge
            var canvas = document.getElementById('builder-canvas');
            if (canvas) {
                var innerEl = canvas.firstElementChild;
                var padding = 300;
                var groupRect = dragState.el.getBoundingClientRect();
                var neededW = x + groupRect.width + padding;
                var neededH = y + groupRect.height + padding;
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

            updateGroupEdgePaths(dragState.id, x, y);
        }

        if (connectState) {
            e.preventDefault();
            var canvas = document.getElementById('builder-canvas');
            if (!canvas) return;
            var innerEl = canvas.firstElementChild;
            var canvasRect = innerEl.getBoundingClientRect();
            var tempLine = document.getElementById('temp-edge-line');
            if (tempLine) {
                tempLine.setAttribute('x2', e.clientX - canvasRect.left);
                tempLine.setAttribute('y2', e.clientY - canvasRect.top);
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
                var x = parseFloat(dragState.el.style.left);
                var y = parseFloat(dragState.el.style.top);
                saveGroupPosition(dragState.templateId, dragState.id, x, y);
            }
            dragState = null;
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

    // --- Edge Recalculation (DOM-measured) ---

    function getGroupRectFromDOM(canvasInnerEl, groupEl) {
        var canvasRect = canvasInnerEl.getBoundingClientRect();
        var groupRect = groupEl.getBoundingClientRect();
        var x = groupRect.left - canvasRect.left;
        var y = groupRect.top - canvasRect.top;
        var w = groupRect.width;
        var h = groupRect.height;
        return { x: x, y: y, w: w, h: h, cx: x + w / 2, cy: y + h / 2 };
    }

    function recalculateViewerEdges() {
        var canvas = document.getElementById('viewer-canvas');
        if (!canvas) return;
        var innerEl = canvas.firstElementChild;
        if (!innerEl) return;

        document.querySelectorAll('.viewer-edge').forEach(function (edge) {
            var fromGid = edge.dataset.fromGroup;
            var toGid = edge.dataset.toGroup;
            if (!fromGid || !toGid) return;

            var fromGroup = canvas.querySelector('.viewer-group[data-group-id="' + fromGid + '"]');
            var toGroup = canvas.querySelector('.viewer-group[data-group-id="' + toGid + '"]');
            if (!fromGroup || !toGroup) return;

            var from = getGroupRectFromDOM(innerEl, fromGroup);
            var to = getGroupRectFromDOM(innerEl, toGroup);
            var ep = computeEdgeEndpoints(from, to);
            edge.setAttribute('d', calculateBezierPath(ep.x1, ep.y1, ep.x2, ep.y2));
        });
    }

    function recalculateBuilderEdges() {
        var canvas = document.getElementById('builder-canvas');
        if (!canvas) return;
        var innerEl = canvas.firstElementChild;
        if (!innerEl) return;

        document.querySelectorAll('.builder-edge').forEach(function (edge) {
            var fromGid = edge.dataset.fromGroup;
            var toGid = edge.dataset.toGroup;
            if (!fromGid || !toGid) return;

            var fromGroup = canvas.querySelector('.builder-group[data-group-id="' + fromGid + '"]');
            var toGroup = canvas.querySelector('.builder-group[data-group-id="' + toGid + '"]');
            if (!fromGroup || !toGroup) return;

            var from = getGroupRectFromDOM(innerEl, fromGroup);
            var to = getGroupRectFromDOM(innerEl, toGroup);
            var ep = computeEdgeEndpoints(from, to);
            setEdgePath(edge, calculateBezierPath(ep.x1, ep.y1, ep.x2, ep.y2));
        });
    }

    document.addEventListener('htmx:afterSettle', function (e) {
        var targetId = e.detail.target && e.detail.target.id;
        if (!targetId) return;

        if (targetId === 'hx-page-container' || targetId === 'viewer-canvas' || targetId === 'builder-canvas') {
            requestAnimationFrame(function () {
                recalculateViewerEdges();
                recalculateBuilderEdges();
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
                    '<p class="mt-2 text-sm text-slate-600 dark:text-slate-400">' + message + '</p>' +
                '</div>' +
                '<div class="px-6 py-3 bg-slate-50 dark:bg-slate-800 border-t border-slate-200 dark:border-slate-700 flex items-center justify-end gap-3">' +
                    '<button type="button" data-dismiss class="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-600 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 cursor-pointer">Cancel</button>' +
                    '<button type="button" data-confirm class="px-4 py-2 text-sm font-semibold text-white bg-red-500 rounded-lg hover:bg-red-600 cursor-pointer">Delete</button>' +
                '</div>' +
            '</div>';

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

        showConfirmModal(message, function () {
            htmx.ajax('DELETE', url, {
                target: '#builder-canvas',
                swap: 'innerHTML'
            });
        });
    });

    // --- View Mode Toggle (icon-only vs icon+text) ---

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.view-mode-toggle');
        if (!btn) return;
        e.preventDefault();

        // Toggle all groups on the page
        document.querySelectorAll('.view-list').forEach(function (el) {
            el.classList.toggle('hidden');
        });
        document.querySelectorAll('.view-icons').forEach(function (el) {
            el.classList.toggle('hidden');
        });

        // Recalculate edge paths after DOM updates to new dimensions
        requestAnimationFrame(function () {
            recalculateViewerEdges();
            recalculateBuilderEdges();
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

        htmx.ajax('DELETE', `/api/templates/${templateId}/builder/edges/${edgeId}`, {
            target: '#builder-canvas',
            swap: 'innerHTML'
        });
    });

    // --- Wheel scroll on canvas ---

    document.addEventListener('wheel', function (e) {
        var canvas = e.target.closest('#builder-canvas') || e.target.closest('#viewer-canvas');
        if (!canvas) return;
        e.preventDefault();
        canvas.scrollLeft += e.deltaX || 0;
        canvas.scrollTop += e.deltaY || 0;
    }, { passive: false });

    // --- Initial edge recalculation on page load ---

    document.addEventListener('DOMContentLoaded', function () {
        requestAnimationFrame(function () {
            recalculateViewerEdges();
            recalculateBuilderEdges();
        });
    });
})();
