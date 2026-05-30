MAIN     = report
LATEX    = pdflatex
LATEXMK  = latexmk
OUT_DIR  = build

.PHONY: all pdf clean distclean watch

# default: build with latexmk if available, fall back to pdflatex
all: pdf

# preferred: latexmk handles multiple passes automatically
pdf:
	@if command -v latexmk >/dev/null 2>&1; then \
		mkdir -p $(OUT_DIR); \
		$(LATEXMK) -pdf -interaction=nonstopmode \
			-output-directory=$(OUT_DIR) $(MAIN).tex; \
	else \
		$(MAKE) pdf-manual; \
	fi

# manual fallback: three passes for TOC, references, and final
# first two passes may exit non-zero due to unresolved references — ignore them
pdf-manual:
	@mkdir -p $(OUT_DIR)
	-$(LATEX) -interaction=nonstopmode -output-directory=$(OUT_DIR) $(MAIN).tex
	-$(LATEX) -interaction=nonstopmode -output-directory=$(OUT_DIR) $(MAIN).tex
	$(LATEX) -interaction=nonstopmode -output-directory=$(OUT_DIR) $(MAIN).tex
	@echo "Output: $(OUT_DIR)/$(MAIN).pdf"

# open the PDF after building (macOS)
open: pdf
	open $(OUT_DIR)/$(MAIN).pdf

# continuous rebuild on file changes (requires latexmk)
watch:
	@mkdir -p $(OUT_DIR)
	$(LATEXMK) -pdf -pvc -interaction=nonstopmode \
		-output-directory=$(OUT_DIR) $(MAIN).tex

# remove auxiliary files, keep PDF
clean:
	@if command -v latexmk >/dev/null 2>&1; then \
		$(LATEXMK) -c -output-directory=$(OUT_DIR) $(MAIN).tex; \
	else \
		rm -f $(OUT_DIR)/$(MAIN).aux \
		      $(OUT_DIR)/$(MAIN).log \
		      $(OUT_DIR)/$(MAIN).toc \
		      $(OUT_DIR)/$(MAIN).lof \
		      $(OUT_DIR)/$(MAIN).lot \
		      $(OUT_DIR)/$(MAIN).out \
		      $(OUT_DIR)/$(MAIN).fls \
		      $(OUT_DIR)/$(MAIN).fdb_latexmk \
		      $(OUT_DIR)/chapters/*.aux; \
	fi

# remove everything including the PDF
distclean: clean
	rm -rf $(OUT_DIR)
