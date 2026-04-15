import {
  getPaper, updatePaper, getPaperSummaries, getPaperDocuments,
  approveSummary, rejectSummary, createSummarizeJob, queueDocumentProcessing,
  createPaperDocument, getPaperAnnotations, createAnnotation,
  updateAnnotation, deleteAnnotation
} from '../api.js';
import {
  h, clear, loading, badge, formatAuthors, formatDate, formatDateTime,
  jsonBlock, toast, emptyState
} from '../components.js';
import { renderAnnotationSidebar, createHighlightButton } from '../components/annotations.js';
import { renderCitationGraph } from '../components/CitationGraph.js';

export async function render(container, { navigate, params }) {
  const paperId = params.id;
  clear(container);
  container.appendChild(loading('Loading paper'));

  try {
    const [paper, summaries, documents, annotations] = await Promise.all([
      getPaper(paperId),
      getPaperSummaries(paperId).catch(() => []),
      getPaperDocuments(paperId).catch(() => []),
      getPaperAnnotations(paperId).catch(() => []),
    ]);

    clear(container);

    // Back link
    container.appendChild(
      h('a', {
        className: 'detail-back',
        href: '#/papers',
        onClick: (e) => { e.preventDefault(); navigate('/papers'); }
      }, '\u2190 PAPERS')
    );

    // Title + status
    const headerRow = h('div', { className: 'flex items-center gap-4 mb-6' },
      h('h1', { className: 'detail-title', style: 'margin-bottom:0' }, paper.title),
      badge(paper.status),
    );
    container.appendChild(headerRow);

    // Meta row
    const meta = h('div', { className: 'detail-meta' });
    const metaItems = [
      ['Authors', formatAuthors(paper.authors, 10)],
      ['Year', paper.year ? String(paper.year) : '\u2014'],
      ['Venue', paper.venue || '\u2014'],
      ['Citations', String(paper.citationCount)],
      ['Source', paper.source],
      ['Added', formatDate(paper.createdAt)],
    ];
    if (paper.doi) metaItems.push(['DOI', paper.doi]);
    if (paper.semanticScholarId) metaItems.push(['S2 ID', paper.semanticScholarId]);

    for (const [label, value] of metaItems) {
      meta.appendChild(
        h('div', { className: 'meta-item' },
          h('span', { className: 'meta-label' }, label),
          h('span', { className: 'meta-value' }, value),
        )
      );
    }
    container.appendChild(meta);

    // Tabs
    const tabs = h('div', { className: 'tabs' });
    const tabNames = ['Overview', 'Documents', 'Citation Graph', 'Annotations'];
    let activeTab = 'Overview';

    const tabBtns = tabNames.map(name => {
      const tab = h('button', {
        className: `tab ${activeTab === name ? 'active' : ''}`,
        onClick: () => {
          activeTab = name;
          tabBtns.forEach((t, i) => t.classList.toggle('active', tabNames[i] === name));
          overviewContent.style.display = name === 'Overview' ? '' : 'none';
          docsContent.style.display = name === 'Documents' ? '' : 'none';
          graphContent.style.display = name === 'Citation Graph' ? '' : 'none';
          annotationContent.style.display = name === 'Annotations' ? '' : 'none';
        },
      }, name);
      if (name === 'Documents') {
        tab.appendChild(h('span', { className: 'tab-count' }, documents.length));
      }
      if (name === 'Annotations') {
        tab.appendChild(h('span', { className: 'tab-count' }, annotations.length));
      }
      return tab;
    });
    tabs.appendChild(...tabBtns);
    container.appendChild(tabs);

    // Overview content
    const overviewContent = h('div', { id: 'tab-overview' });
    const docsContent = h('div', { id: 'tab-documents', style: 'display:none' });
    const graphContent = h('div', { id: 'tab-graph', style: 'display:none' });
    const annotationContent = h('div', { id: 'tab-annotations', style: 'display:none' });

    // Abstract
    if (paper.abstract) {
      overviewContent.appendChild(
        h('div', { className: 'section' },
          h('div', { className: 'section-header' },
            h('h2', { className: 'section-title' }, 'Abstract'),
          ),
          h('p', { className: 'detail-abstract' }, paper.abstract),
        )
      );
    }

    // Actions
    const actions = h('div', { className: 'page-actions mb-8' });
    const summarizeBtn = h('button', {
      className: 'btn btn-primary btn-sm',
      onClick: async () => {
        try {
          summarizeBtn.disabled = true;
          summarizeBtn.textContent = 'CREATING JOB\u2026';
          await createSummarizeJob({ paperId });
          toast('Summarize job created', 'success');
          navigate(`/papers/${paperId}`);
        } catch (err) {
          toast(err.message, 'error');
          summarizeBtn.disabled = false;
          summarizeBtn.textContent = 'SUMMARIZE';
        }
      },
    }, 'SUMMARIZE');
    actions.appendChild(summarizeBtn);

    const statusBtns = ['Draft', 'Imported', 'Processing', 'Ready', 'Archived'];
    for (const s of statusBtns) {
      if (s === paper.status) continue;
      actions.appendChild(
        h('button', {
          className: 'btn btn-secondary btn-sm',
          onClick: async () => {
            try {
              await updatePaper(paperId, { status: s });
              toast(`Status changed to ${s}`, 'success');
              navigate(`/papers/${paperId}`);
            } catch (err) {
              toast(err.message, 'error');
            }
          },
        }, `SET ${s.toUpperCase()}`)
      );
    }
    overviewContent.appendChild(actions);

    // Summaries section
    const summariesSection = h('div', { className: 'section' });
    summariesSection.appendChild(
      h('div', { className: 'section-header' },
        h('h2', { className: 'section-title' }, `Summaries (${summaries.length})`),
      )
    );

    if (summaries.length === 0) {
      summariesSection.appendChild(emptyState('No summaries', 'Create a summarize job to generate one'));
    } else {
      for (const summary of summaries) {
        summariesSection.appendChild(renderSummaryCard(summary, paperId, navigate));
      }
    }
    overviewContent.appendChild(summariesSection);

    // Metadata
    if (paper.metadata) {
      overviewContent.appendChild(
        h('div', { className: 'section' },
          h('div', { className: 'section-header' },
            h('h2', { className: 'section-title' }, 'Metadata'),
          ),
          jsonBlock(paper.metadata),
        )
      );
    }

    // Documents tab content
    const addDocForm = h('div', { className: 'section' });
    addDocForm.appendChild(
      h('div', { className: 'section-header' },
        h('h2', { className: 'section-title' }, 'Add Document'),
      )
    );

    const urlField = h('div', { className: 'field-group' },
      h('label', { className: 'field-label' }, 'Source URL'),
      h('input', { type: 'url', className: 'input', id: 'doc-url-input', placeholder: 'https://example.com/paper.pdf' })
    );
    addDocForm.appendChild(urlField);

    const addDocActions = h('div', { className: 'page-actions' });
    const addDocBtn = h('button', {
      className: 'btn btn-primary btn-sm',
      onClick: async () => {
        const urlInput = document.getElementById('doc-url-input');
        const sourceUrl = urlInput.value.trim();
        if (!sourceUrl) {
          toast('Please enter a source URL', 'error');
          return;
        }
        try {
          addDocBtn.disabled = true;
          addDocBtn.textContent = 'ADDING\u2026';
          await createPaperDocument(paperId, { sourceUrl });
          toast('Document added', 'success');
          navigate(`/papers/${paperId}`);
        } catch (err) {
          toast(err.message, 'error');
          addDocBtn.disabled = false;
          addDocBtn.textContent = 'ADD DOCUMENT';
        }
      },
    }, 'ADD DOCUMENT');
    addDocActions.appendChild(addDocBtn);
    addDocForm.appendChild(addDocActions);

    docsContent.appendChild(addDocForm);

    // Documents list
    const docsSection = h('div', { className: 'section' });
    docsSection.appendChild(
      h('div', { className: 'section-header' },
        h('h2', { className: 'section-title' }, `Documents (${documents.length})`),
      )
    );

    if (documents.length === 0) {
      docsSection.appendChild(emptyState('No documents', 'Add a document URL above'));
    } else {
      for (const doc of documents) {
        docsSection.appendChild(renderDocumentCard(doc, paperId, navigate));
      }
    }
    docsContent.appendChild(docsSection);

    container.appendChild(overviewContent);
    container.appendChild(docsContent);
    container.appendChild(graphContent);
    container.appendChild(annotationContent);

    if (paper.semanticScholarId) {
      renderCitationGraph(graphContent, { paperId, navigate });
    } else {
      graphContent.appendChild(emptyState('No Semantic Scholar ID', 'Import this paper from Semantic Scholar to view its citation graph'));
    }

    const handleCreateAnnotation = async (data) => {
      try {
        await createAnnotation(paperId, data);
        toast('Annotation created', 'success');
      } catch (err) {
        toast(err.message, 'error');
      }
    };

    const handleUpdateAnnotation = async (id, data) => {
      try {
        await updateAnnotation(id, data);
      } catch (err) {
        toast(err.message, 'error');
      }
    };

    const handleDeleteAnnotation = async (id) => {
      try {
        await deleteAnnotation(id);
      } catch (err) {
        toast(err.message, 'error');
      }
    };

    renderAnnotationSidebar(annotationContent, {
      paperId,
      annotations,
      onCreate: handleCreateAnnotation,
      onUpdate: handleUpdateAnnotation,
      onDelete: handleDeleteAnnotation
    });

  } catch (err) {
    clear(container);
    if (err.name === 'AbortError') return;
    container.appendChild(emptyState('Paper not found', err.message));
    toast(err.message, 'error');
  }
}

function renderSummaryCard(summary, paperId, navigate) {
  const card = h('div', { className: 'summary-card' });

  const header = h('div', { className: 'summary-card-header' },
    h('div', { className: 'flex items-center gap-3' },
      badge(summary.status),
      h('span', { className: 'summary-card-meta' }, `Model: ${summary.modelName || '\u2014'}`),
      summary.promptVersion ? h('span', { className: 'summary-card-meta' }, `v${summary.promptVersion}`) : null,
    ),
    h('span', { className: 'summary-card-meta' }, formatDateTime(summary.createdAt)),
  );
  card.appendChild(header);

  // Summary content
  if (summary.summary) {
    const content = typeof summary.summary === 'string' ? summary.summary : JSON.stringify(summary.summary, null, 2);
    card.appendChild(h('div', { className: 'summary-card-body' },
      h('pre', { className: 'json-block', style: 'max-height:200px' }, content),
    ));
  }

  // Review info
  if (summary.reviewedBy) {
    card.appendChild(h('div', { className: 'summary-card-meta mt-4' },
      `Reviewed by ${summary.reviewedBy} on ${formatDateTime(summary.reviewedAt)}`,
      summary.reviewNotes ? ` \u2014 ${summary.reviewNotes}` : '',
    ));
  }

  // Actions
  if (summary.status === 'Generated' || summary.status === 'Pending') {
    const actions = h('div', { className: 'summary-card-actions' });
    actions.appendChild(
      h('button', {
        className: 'btn btn-primary btn-sm',
        onClick: async () => {
          try {
            await approveSummary(summary.id, {});
            toast('Summary approved', 'success');
            navigate(`/papers/${paperId}`);
          } catch (err) {
            toast(err.message, 'error');
          }
        },
      }, 'APPROVE')
    );
    actions.appendChild(
      h('button', {
        className: 'btn btn-danger btn-sm',
        onClick: async () => {
          try {
            await rejectSummary(summary.id, {});
            toast('Summary rejected', 'success');
            navigate(`/papers/${paperId}`);
          } catch (err) {
            toast(err.message, 'error');
          }
        },
      }, 'REJECT')
    );
    card.appendChild(actions);
  }

  return card;
}

function renderDocumentCard(doc, paperId, navigate) {
  const card = h('div', { className: 'summary-card' });

  const header = h('div', { className: 'summary-card-header' },
    h('div', { className: 'flex items-center gap-3' },
      badge(doc.status),
      h('span', { className: 'summary-card-meta' }, doc.mediaType || 'Document'),
    ),
    h('span', { className: 'summary-card-meta' }, formatDateTime(doc.updatedAt)),
  );
  card.appendChild(header);

  if (doc.sourceUrl) {
    card.appendChild(h('div', { className: 'cell-mono truncate', style: 'max-width:500px;font-size:11px;color:var(--c-text-secondary);margin-bottom:var(--s-3)' }, doc.sourceUrl));
  }

  if (doc.lastError) {
    card.appendChild(h('div', { className: 'summary-card-meta', style: 'color:var(--c-red);margin-bottom:var(--s-3)' }, `Error: ${doc.lastError}`));
  }

  if (doc.extractedText) {
    const toggleBtn = h('button', {
      className: 'btn btn-secondary btn-sm',
      onClick: () => {
        const panel = document.getElementById(`doc-text-${doc.id}`);
        if (panel.hidden) {
          panel.hidden = false;
          toggleBtn.textContent = 'HIDE TEXT';
        } else {
          panel.hidden = true;
          toggleBtn.textContent = 'VIEW TEXT';
        }
      },
    }, 'VIEW TEXT');
    card.appendChild(h('div', { style: 'margin-bottom:var(--s-3)' }, toggleBtn));

    const textPanel = h('div', {
      id: `doc-text-${doc.id}`,
      hidden: true,
      className: 'summary-card-body',
    },
      h('pre', {
        className: 'json-block',
        style: 'max-height:400px;font-size:11px;white-space:pre-wrap'
      }, doc.extractedText)
    );
    card.appendChild(textPanel);
  } else if (doc.status === 'Extracted' || doc.status === 'Downloaded') {
    card.appendChild(h('div', { className: 'summary-card-meta', style: 'margin-bottom:var(--s-3)' }, 'No extracted text available'));
  }

  if (doc.status !== 'Extracted' && doc.status !== 'Failed') {
    const processBtn = h('button', {
      className: 'btn btn-primary btn-sm',
      onClick: async () => {
        try {
          processBtn.disabled = true;
          processBtn.textContent = 'QUEUING\u2026';
          await queueDocumentProcessing(paperId, doc.id, { force: false });
          toast('Processing queued', 'success');
          navigate(`/papers/${paperId}`);
        } catch (err) {
          toast(err.message, 'error');
          processBtn.disabled = false;
          processBtn.textContent = 'PROCESS';
        }
      },
    }, 'PROCESS');
    card.appendChild(h('div', { className: 'summary-card-actions' }, processBtn));
  }

  return card;
}
