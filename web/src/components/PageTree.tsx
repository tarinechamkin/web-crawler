import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import type { PageNode } from '../api/types';
import { ErrorMessage, formatRatio, Loading } from './common';

export function PageTree({ jobId, root }: { jobId: string; root: PageNode }) {
  return (
    <div className="card">
      <p className="muted legend">
        Each page sits under the page that first linked to it. <strong>Ratio</strong> = links staying on the
        starting domain ÷ all outgoing links.
      </p>
      <ul className="tree">
        <TreeNode jobId={jobId} node={root} />
      </ul>
    </div>
  );
}

/** Renders itself for each child — a recursive component, like a recursive Angular template. */
function TreeNode({ jobId, node }: { jobId: string; node: PageNode }) {
  const [expanded, setExpanded] = useState(node.depth === 0); // root open, the rest collapsed
  const [showLinks, setShowLinks] = useState(false);
  const hasChildren = node.children.length > 0;

  return (
    <li>
      <div className="node">
        <button
          className="toggle"
          onClick={() => setExpanded(!expanded)}
          disabled={!hasChildren}
          aria-label={expanded ? 'Collapse' : 'Expand'}
        >
          {hasChildren ? (expanded ? '▾' : '▸') : '·'}
        </button>
        <a href={node.url} target="_blank" rel="noreferrer" className="url">{node.url}</a>
        {node.error ? (
          <span className="badge badge-failed" title={node.error}>{node.error}</span>
        ) : node.outgoingLinkCount === 0 ? (
          <span className="ratio muted" title="No outgoing links; the ratio is defined as 0">ratio 0 (no links)</span>
        ) : (
          <span className="ratio" title={`${node.internalLinkCount} of ${node.outgoingLinkCount} links are internal`}>
            ratio {formatRatio(node.domainLinkRatio)}
            <span className="muted"> ({node.internalLinkCount}/{node.outgoingLinkCount})</span>
          </span>
        )}
        {node.outgoingLinkCount > 0 && (
          <button className="link-button" onClick={() => setShowLinks(!showLinks)}>
            {showLinks ? 'hide links' : 'links'}
          </button>
        )}
        {hasChildren && <span className="muted">{node.children.length} child page{node.children.length === 1 ? '' : 's'}</span>}
      </div>

      {showLinks && <LinkList jobId={jobId} pageId={node.pageId} />}

      {expanded && hasChildren && (
        <ul className="tree">
          {node.children.map((child) => (
            <TreeNode key={child.pageId} jobId={jobId} node={child} />
          ))}
        </ul>
      )}
    </li>
  );
}

/** Outgoing links are loaded only when asked for, keeping the tree response small. */
function LinkList({ jobId, pageId }: { jobId: string; pageId: number }) {
  const links = useQuery({
    queryKey: ['links', jobId, pageId],
    queryFn: () => api.getPageLinks(jobId, pageId),
  });

  if (links.isPending) return <Loading label="Loading links…" />;
  if (links.isError) return <ErrorMessage error={links.error} />;
  return (
    <ul className="links">
      {links.data.links.map((link) => (
        <li key={link.url}>
          <span className={link.isInternal ? 'tag tag-internal' : 'tag tag-external'}>
            {link.isInternal ? 'internal' : 'external'}
          </span>
          <a href={link.url} target="_blank" rel="noreferrer">{link.url}</a>
        </li>
      ))}
    </ul>
  );
}
