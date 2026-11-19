document.addEventListener('DOMContentLoaded', function () {
    const feeSelect = document.getElementById('Member_MembershipFeeId');
    const guardianSection = document.getElementById('guardianSection');
    const familySection = document.getElementById('familySection');
    const familyContainer = document.getElementById('familyMembersContainer');
    const template = document.getElementById('familyMemberTemplate');
    const addBtn = document.getElementById('addFamilyMemberBtn');
    let familyMemberIndex = 0;

    if (template) {
        // A template is not a family member and must never be submitted.
        template.querySelectorAll('input, select').forEach(function (el) {
            el.disabled = true;
        });
    }

    function getSelectedFeeCategory() {
        if (!feeSelect) return null;
        const selectedOption = feeSelect.options[feeSelect.selectedIndex];
        if (!selectedOption || !selectedOption.parentElement) return null;
        return selectedOption.parentElement.getAttribute('label') || null;
    }

    function hasGuardianData() {
        if (!guardianSection || guardianSection.dataset.keepVisibleIfPopulated !== 'true') {
            return false;
        }
        const memberId = document.getElementById('Member_GuardianMemberId')?.value;
        const gFirst = document.getElementById('Member_GuardianFirstName')?.value;
        const gLast = document.getElementById('Member_GuardianLastName')?.value;
        const gEmail = document.getElementById('Member_GuardianEmail')?.value;
        const gPhone = document.getElementById('Member_GuardianPhone')?.value;
        return Boolean(memberId || gFirst || gLast || gEmail || gPhone);
    }

    function toggleSections() {
        const category = getSelectedFeeCategory();
        if (guardianSection) {
            const isYouth = category === '1' || category === 'Youth';
            guardianSection.style.display = (isYouth || hasGuardianData()) ? 'block' : 'none';
        }
        if (familySection) {
            const isFamily = category === '2' || category === 'Family';
            familySection.style.display = isFamily ? 'block' : 'none';
        }
    }

    function addFamilyMemberRow(data) {
        if (!template || !familyContainer) return;
        data = data || {};
        const clone = template.cloneNode(true);
        clone.style.display = 'flex';
        clone.removeAttribute('id');

        const index = familyMemberIndex++;
        clone.querySelectorAll('input, select').forEach(function (el) {
            el.disabled = false;
            const name = el.getAttribute('name');
            if (name) {
                const updatedName = name.replace('INDEX', index);
                el.setAttribute('name', updatedName);
                if (updatedName !== 'FamilyMembers.Index') {
                    el.setAttribute('id', 'FamilyMembers_' + index + '_' + updatedName.split('.').pop());
                }
            }
            if (el.getAttribute('name') === 'FamilyMembers.Index') {
                el.value = index;
            }
        });

        if (data.firstName) clone.querySelector('[name$=".FirstName"]').value = data.firstName;
        if (data.lastName) clone.querySelector('[name$=".LastName"]').value = data.lastName;
        if (data.email) clone.querySelector('[name$=".Email"]').value = data.email;
        if (data.birthDay) clone.querySelector('[name$=".BirthDay"]').value = String(data.birthDay);
        if (data.birthMonth) clone.querySelector('[name$=".BirthMonth"]').value = String(data.birthMonth);
        if (data.birthYear) clone.querySelector('[name$=".BirthYear"]').value = String(data.birthYear);
        if (data.joinedAt) {
            const joinedInput = clone.querySelector('[name$=".JoinedAt"]');
            if (joinedInput) joinedInput.value = data.joinedAt;
        }

        if (data.errors && data.errors.length > 0) {
            const errorSpan = clone.querySelector('.family-row-errors');
            if (errorSpan) {
                errorSpan.style.display = 'block';
                errorSpan.textContent = data.errors.join(' ');
            }
        } else if (data.hasErrors) {
            const errorSpan = clone.querySelector('.family-row-errors');
            if (errorSpan) {
                errorSpan.style.display = 'block';
                errorSpan.textContent = 'Bitte prüfen Sie die Eingaben';
            }
        }

        const removeBtn = clone.querySelector('.remove-family-row');
        if (removeBtn) {
            removeBtn.addEventListener('click', function () {
                clone.remove();
            });
        }

        familyContainer.appendChild(clone);
    }

    if (familySection && familySection.dataset.initialRows) {
        try {
            const initialRows = JSON.parse(familySection.dataset.initialRows);
            if (Array.isArray(initialRows)) {
                initialRows.forEach(addFamilyMemberRow);
            }
        } catch (err) {
            console.error('Failed to parse initial family rows', err);
        }
    }

    if (feeSelect) {
        feeSelect.addEventListener('change', toggleSections);
    }
    if (addBtn) {
        addBtn.addEventListener('click', function () {
            addFamilyMemberRow({});
        });
    }
    toggleSections();
});
